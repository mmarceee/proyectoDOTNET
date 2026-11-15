using System.Globalization;
using System.Net.Mail;
using Logistica.Http.Contracts.Seguimiento;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;
using Logistica.Modules.Seguimiento.Application.Features.NotificarDestinatario;
using Logistica.Modules.Seguimiento.Domain.Notificaciones;
using Logistica.Modules.Seguimiento.Domain.Reprogramaciones;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Application.Features.SolicitarReprogramacion;

// CU-61 desde el seguimiento público. El inquilino ya salió del token (InquilinoDelToken). La reprogramación la hace
// Envíos, dueño de la transición (decisión H2); acá sólo se traduce para la destinataria y, al terminar, se borra su
// seguimiento de la caché para que vea la nueva ventana al instante (ADR-0006, sección 2.5: después de confirmar).
// Requerimiento especial 4: el enlace solo no alcanza para reprogramar. Antes, el destinatario confirma con un código que
// le llega al correo o, si el envío no tiene correo, con los últimos 4 dígitos de su teléfono.
internal sealed class SolicitarReprogramacionHandler(IEnviosModuleApi envios, ICacheDistribuida cache,
    IVerificacionesReprogramacionRepository verificaciones, SecretoVerificacion secretos, IEmisorCorreo emisor,
    IIdentidadesOperadorRepository identidades, TokenSeguimiento tokens, DireccionPublica direccion, ICurrentTenant tenant,
    TimeProvider reloj)
{
    // Pedidos de verificación por envío y por hora: con el teléfono, 5 pedidos de 3 intentos son 15 de 10.000 posibles.
    public const int MaxPedidosPorHora = 5;
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-UY");

    // null si el envío no existe o es de otro operador: el endpoint responde igual que a un enlace inválido.
    public async Task<OpcionesReprogramacionResponse?> OpcionesAsync(Guid envioId, CancellationToken ct)
    {
        if (await envios.OpcionesReprogramacionAsync(envioId, ct) is not { } opciones)
        {
            return null;
        }

        return new OpcionesReprogramacionResponse(opciones.Select(o => new OpcionReprogramacionResponse(o.Fecha, o.FranjaId,
            Texto(o))).ToList());
    }

    // Paso 3: el destinatario eligió fecha y franja. Se anula cualquier verificación anterior y se crea una nueva: con
    // correo, se le manda el código; sin correo, tendrá que escribir los últimos 4 dígitos de su teléfono.
    public async Task<VerificacionReprogramacionResponse?> SolicitarVerificacionAsync(Guid envioId,
        SolicitarVerificacionRequest pedido, CancellationToken ct)
    {
        if (await envios.OpcionesReprogramacionAsync(envioId, ct) is not { } opciones
            || await envios.ContactoDestinatarioAsync(envioId, ct) is not { } contacto)
        {
            return null;
        }

        // Sólo se verifica una opción que se puede elegir: no se mandan códigos para fechas imposibles.
        var opcion = opciones.SingleOrDefault(o => o.Fecha == pedido.Fecha && o.FranjaId == pedido.FranjaId)
            ?? throw new DomainException("Esa fecha ya no está disponible. Elegí otra.");

        var ahora = reloj.GetUtcNow();
        if (await verificaciones.ContarDesdeAsync(envioId, ahora.AddHours(-1), ct) >= MaxPedidosPorHora)
            throw new DomainException("Pediste demasiados códigos. Probá de nuevo dentro de una hora.");

        foreach (var anterior in await verificaciones.ListarAbiertasAsync(envioId, ct))
        {
            anterior.Anular();
        }

        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("El enlace no tiene operador.");
        if (CorreoValido(contacto) is { } email)
        {
            var codigo = SecretoVerificacion.NuevoCodigo();
            var verificacion = Crear(operadorId, envioId, pedido, MedioVerificacion.Correo, codigo, ahora);
            await verificaciones.GuardarAsync(ct);

            var marca = await MarcaAsync(operadorId, ct);
            var resultado = await emisor.EnviarAsync(new MensajeCorreo(verificacion.Id, email, null,
                $"Tu código para cambiar la entrega: {codigo}", $"Tu código es {codigo}",
                $"Lo pediste para recibir tu envío el {Texto(opcion)}. Vence en 10 minutos. " +
                "Si no fuiste vos, ignorá este correo: tu envío no cambia.",
                Enlace(operadorId, envioId), marca), ct);
            if (!resultado.Exitoso)
            {
                verificacion.Anular();
                await verificaciones.GuardarAsync(ct);
                throw new DomainException("No pudimos enviarte el código. Probá de nuevo en unos minutos.");
            }

            return new VerificacionReprogramacionResponse(nameof(MedioVerificacion.Correo), SecretoVerificacion.Enmascarar(email));
        }

        var digitos = SecretoVerificacion.UltimosDigitos(contacto.Telefono)
            ?? throw new DomainException("No podemos verificar tu identidad. Comunicate con el comercio para cambiar la entrega.");
        Crear(operadorId, envioId, pedido, MedioVerificacion.Telefono, digitos, ahora);
        await verificaciones.GuardarAsync(ct);
        return new VerificacionReprogramacionResponse(nameof(MedioVerificacion.Telefono), null);
    }

    // Paso 4: se comprueba el código contra la última verificación y recién ahí se reprograma. false si el envío no
    // existe o es de otro operador. Un código incorrecto o una regla rota (A1, A2, A3) salen como DomainException (400).
    public async Task<bool> ReprogramarAsync(Guid envioId, SolicitarReprogramacionRequest pedido, CancellationToken ct)
    {
        var verificacion = await verificaciones.UltimaAsync(envioId, ct)
            ?? throw new DomainException("Primero pedí el código para confirmar el cambio.");

        var ahora = reloj.GetUtcNow();
        var resultado = verificacion.Comprobar(pedido.Fecha, pedido.FranjaId, secretos.Hash(envioId, pedido.Codigo), ahora);
        await verificaciones.GuardarAsync(ct); // un intento fallido queda contado antes de responder
        if (resultado != ResultadoVerificacion.Valida)
            throw new DomainException(Motivo(resultado, verificacion));

        // Lo que necesita el aviso se lee antes de reprogramar: Envíos confirma su transacción al reprogramar (unidad de
        // trabajo compartida, ADR-0003) y una consulta posterior quedaría atada a la transacción ya cerrada.
        var aviso = await PrepararAvisoAsync(envioId, verificacion, ct);

        if (!await envios.ReprogramarPorDestinatarioAsync(envioId, pedido.Fecha, pedido.FranjaId, ct))
        {
            return false;
        }

        verificacion.MarcarUsada(ahora);
        await verificaciones.GuardarAsync(ct);
        await cache.InvalidarAsync(ConsultarSeguimientoHandler.Cache, ConsultarSeguimientoHandler.Clave(envioId), ct);
        // Un fallo del correo no deshace la reprogramación, que ya se aplicó: el emisor informa el error sin lanzarlo.
        if (aviso is not null) await emisor.EnviarAsync(aviso, ct);
        return true;
    }

    // Si alguien más hubiera reprogramado, el destinatario se entera: el correo no impide el cambio, pero lo hace visible.
    // null si el envío no tiene correo.
    private async Task<MensajeCorreo?> PrepararAvisoAsync(Guid envioId, VerificacionReprogramacion verificacion,
        CancellationToken ct)
    {
        if (await envios.ContactoDestinatarioAsync(envioId, ct) is not { } contacto || CorreoValido(contacto) is not { } email)
        {
            return null;
        }

        var opcion = (await envios.OpcionesReprogramacionAsync(envioId, ct))?
            .FirstOrDefault(o => o.Fecha == verificacion.Fecha && o.FranjaId == verificacion.FranjaId);
        var cuando = opcion is null ? verificacion.Fecha.ToString("dddd d 'de' MMMM", Cultura) : Texto(opcion);
        var marca = await MarcaAsync(verificacion.OperadorId, ct);
        var contactoOperador = string.Join(" · ", new[] { marca.EmailContacto, marca.TelefonoContacto }.Where(c => c is not null));
        return new MensajeCorreo(Guid.CreateVersion7(), email, null,
            "Se cambió la entrega de tu envío", $"Nueva entrega: {cuando}",
            $"Si no fuiste vos, comunicate con {marca.Nombre}" + (contactoOperador.Length > 0 ? $" ({contactoOperador})." : "."),
            Enlace(verificacion.OperadorId, envioId), marca);
    }

    private VerificacionReprogramacion Crear(Guid operadorId, Guid envioId, SolicitarVerificacionRequest pedido,
        MedioVerificacion medio, string secreto, DateTimeOffset ahora)
    {
        var verificacion = VerificacionReprogramacion.Crear(operadorId, envioId, pedido.Fecha, pedido.FranjaId, medio,
            secretos.Hash(envioId, secreto), ahora);
        verificaciones.Agregar(verificacion);
        return verificacion;
    }

    private static string Motivo(ResultadoVerificacion resultado, VerificacionReprogramacion verificacion) => resultado switch
    {
        ResultadoVerificacion.Incorrecta when verificacion.Medio == MedioVerificacion.Telefono =>
            $"Los dígitos no coinciden con tu teléfono. Te quedan {verificacion.IntentosRestantes} intentos.",
        ResultadoVerificacion.Incorrecta => $"El código no es correcto. Te quedan {verificacion.IntentosRestantes} intentos.",
        ResultadoVerificacion.Agotada => "Superaste los intentos. Volvé a elegir la fecha para pedir un código nuevo.",
        ResultadoVerificacion.Vencida => "El código venció. Volvé a elegir la fecha para pedir uno nuevo.",
        ResultadoVerificacion.Usada => "Ese código ya se usó. Volvé a elegir la fecha para pedir uno nuevo.",
        _ => "El código es para otra fecha. Volvé a elegir la fecha para pedir uno nuevo.",
    };

    private static string? CorreoValido(ContactoDestinatario contacto) =>
        contacto.Email is { } email && MailAddress.TryCreate(email.Trim(), out _) ? email.Trim() : null;

    private async Task<MarcaCorreo> MarcaAsync(Guid operadorId, CancellationToken ct) =>
        await identidades.ObtenerAsync(operadorId, ct) is { } identidad
            ? new MarcaCorreo(identidad.Nombre, identidad.ColorPrimario, identidad.EmailContacto, identidad.TelefonoContacto)
            : NotificarDestinatarioHandler.MarcaNeutra;

    private string Enlace(Guid operadorId, Guid envioId) =>
        direccion.Absoluta($"/seguimiento/{tokens.Generar(operadorId, envioId)}");

    private static string Texto(OpcionReprogramacion o) =>
        $"{o.Fecha.ToString("dddd d 'de' MMMM", Cultura)}, de {o.Desde:HH\\:mm} a {o.Hasta:HH\\:mm}";
}

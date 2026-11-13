using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;

namespace Logistica.Modules.Envios.Application.Features.CrearEnvio;

// CU-10: resuelve la cobertura y tarifa cada bulto con el cuadro vigente de Administración.
// La modalidad elegida en el Portal determina las reglas y los ajustes aplicables.
// CU-11 reutiliza el mismo caso de uso para cada envío del archivo: la cotización para la vista previa y el alta
// idempotente por la referencia del comercio para confirmar.
internal sealed class CrearEnvioHandler(IEnvioRepository envios, ICurrentTenant tenant, TimeProvider reloj,
    IRelacionComercialActiva relacion, IUnidadDeTrabajo unidad, IAdministracionModuleApi administracion,
    IIntegrationEventPublisher eventos)
{
    public async Task<Envio> HandleAsync(CrearEnvioCommand command, CancellationToken ct)
    {
        return (await CrearOObtenerAsync(command, ct)).Envio;
    }

    // Con referencia del comercio, repetir el alta devuelve el envío existente sin crear otro (RF 7; CU-11, A3).
    public async Task<AltaEnvio> CrearOObtenerAsync(CrearEnvioCommand command, CancellationToken ct)
    {
        var (operadorId, comercioId) = Inquilino();
        ValidarDatos(command);
        var referencia = Envio.NormalizarReferencia(command.ReferenciaComercio);
        if (referencia is not null && await envios.ObtenerPorReferenciaAsync(comercioId, referencia, command.EsPrueba, ct) is { } existente)
            return new AltaEnvio(existente, Creado: false);

        var cotizacion = await CotizarAsync(command, ct);
        var franja = await ResolverFranjaElegidaAsync(command, cotizacion.ZonaId, ct);
        var numero = await envios.SiguienteNumeroAsync(ct);

        var envio = Envio.Crear(
            operadorId,
            comercioId,
            numero,
            command.Destinatario,
            command.Direccion,
            cotizacion.Bultos,
            command.Origen,
            responsableId: null,
            reloj.GetUtcNow(),
            versionTarifario: cotizacion.VersionTarifario,
            versionReglasId: cotizacion.VersionReglasId,
            modalidad: command.Modalidad,
            referenciaComercio: referencia,
            esPrueba: command.EsPrueba);
        // Con franja y fecha elegidas, Planificación ofrece el envío sólo ese día y en esa franja (CU-40).
        envio.DefinirPlanificacion(cotizacion.ZonaId, franja?.Id, franja is null ? null : command.FechaEntrega);

        try
        {
            await unidad.EjecutarAsync(async () =>
            {
                await relacion.ExigirAsync(comercioId, ct);
                envios.Agregar(envio);
                // T1 también llega a Seguimiento: es el único evento con el comercio en el sobre (CU-64), y lleva la zona
                // y la ventana comprometida para los indicadores de cumplimiento (CU-71).
                eventos.Registrar(EnvioAdmitido.Tipo, new EnvioAdmitido(envio.Id, envio.Numero, envio.CreadoEn,
                    envio.Destinatario.Nombre, envio.EsPrueba ? null : envio.Destinatario.Email, envio.EsPrueba,
                    envio.ZonaId, envio.CreadoEn + cotizacion.PlazoComprometido), envio.CreadoEn);
                await envios.GuardarCambiosAsync(ct);
            }, ct);
        }
        catch (ReferenciaRepetidaException)
        {
            // A4: otra importación creó el mismo envío entre la consulta y el guardado. El índice único evitó el
            // duplicado y la transacción se revirtió entera, evento incluido.
            return new AltaEnvio(await envios.ObtenerPorReferenciaAsync(comercioId, referencia!, command.EsPrueba, ct)
                ?? throw new InvalidOperationException("El índice rechazó la referencia, pero el envío no aparece."), Creado: false);
        }

        return new AltaEnvio(envio, Creado: true);
    }

    // CU-11, vista previa: el envío que ya existe con esa referencia o, si no hay, lo que costaría. No guarda nada.
    public async Task<(Envio? Existente, CotizacionEnvio? Cotizacion)> PrevisualizarAsync(
        CrearEnvioCommand command, CancellationToken ct)
    {
        var (_, comercioId) = Inquilino();
        ValidarDatos(command);
        var referencia = Envio.NormalizarReferencia(command.ReferenciaComercio);
        if (referencia is not null && await envios.ObtenerPorReferenciaAsync(comercioId, referencia, command.EsPrueba, ct) is { } existente)
            return (existente, null);
        return (null, await CotizarAsync(command, ct));
    }

    // Las franjas que se pueden elegir para la dirección y el día: las de la zona del código postal que rigen esa fecha.
    // Sin cobertura del código postal o para un día no elegible, ninguna.
    public async Task<IReadOnlyList<FranjaPlanificacion>> FranjasDisponiblesAsync(string codigoPostal, DateOnly fecha,
        CancellationToken ct)
    {
        if (fecha < await PrimerDiaElegibleAsync(ct))
        {
            return [];
        }

        var recursos = await administracion.ConsultarRecursosPlanificacionAsync(fecha, ct);
        var zona = recursos.Zonas.SingleOrDefault(z => z.CodigosPostales.Contains(codigoPostal.Trim()));
        return zona is null ? [] : recursos.Franjas.Where(f => f.ZonaId == zona.Id).OrderBy(f => f.Desde).ToList();
    }

    // La fecha y la franja van juntas. La franja tiene que regir esa fecha (día de la semana y vigencia) y ser de la
    // zona del código postal, como en CU-19. Se guarda la franja vigente ese día, la que Planificación resuelve.
    private async Task<FranjaPlanificacion?> ResolverFranjaElegidaAsync(CrearEnvioCommand command, Guid zonaId,
        CancellationToken ct)
    {
        if (command.FechaEntrega is null && command.FranjaId is null)
        {
            return null;
        }

        if (command.FechaEntrega is not DateOnly fecha || command.FranjaId is not Guid franjaId)
        {
            throw new DomainException("Elegí la fecha y la franja de entrega juntas, o ninguna de las dos.");
        }

        if (fecha < await PrimerDiaElegibleAsync(ct))
        {
            throw new DomainException("La fecha de entrega tiene que ser posterior a hoy.");
        }

        var franja = (await administracion.ResolverFranjasAsync([franjaId], fecha, ct)).SingleOrDefault();
        if (franja is null || franja.ZonaId != zonaId)
        {
            throw new DomainException("Esa franja no está disponible ese día en la zona de la dirección.");
        }

        return franja;
    }

    // Desde mañana, en el día del operador: el envío todavía tiene que llegar al depósito para salir en una ruta.
    private async Task<DateOnly> PrimerDiaElegibleAsync(CancellationToken ct)
    {
        var contexto = await administracion.ObtenerContextoPlanificacionAsync(ct);
        var zonaHoraria = TimeZoneInfo.FindSystemTimeZoneById(contexto.ZonaHoraria);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zonaHoraria).DateTime).AddDays(1);
    }

    private static void ValidarDatos(CrearEnvioCommand command)
    {
        if (!Enum.IsDefined(command.Modalidad))
            throw new DomainException("Elegí una modalidad válida: Estándar o Urgente.");

        if (command.Bultos.Count == 0)
            throw new DomainException("Un envío debe tener al menos un bulto.");
        if (command.Bultos.Any(b => b.PesoKg <= 0 || b.LargoCm <= 0 || b.AnchoCm <= 0 || b.AltoCm <= 0))
            throw new DomainException("El peso y las medidas del bulto deben ser mayores a cero.");
    }

    private async Task<CotizacionEnvio> CotizarAsync(CrearEnvioCommand command, CancellationToken ct)
    {
        // La versión vigente se conserva para las visitas e intentos posteriores (letra 6.6).
        var reglas = await administracion.ReglasDeEntregaVigentesAsync(ct)
            ?? throw new DomainException("El operador no tiene reglas operativas vigentes: todavía no puede admitir envíos.");

        var recursos = await administracion.ConsultarRecursosPlanificacionAsync(DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime), ct);
        var zona = recursos.Zonas.SingleOrDefault(z => z.CodigosPostales.Contains(command.Direccion.CodigoPostal))
            ?? throw new EnvioNoCotizableException("El operador no cubre el código postal de la dirección.");

        var bultos = new List<DatosBulto>();
        DatosVersionTarifario? versionTarifario = null;
        foreach (var bulto in command.Bultos)
        {
            decimal volumenM3;
            try { volumenM3 = (bulto.LargoCm / 100m) * (bulto.AnchoCm / 100m) * (bulto.AltoCm / 100m); }
            catch (OverflowException) { throw new DomainException($"Las medidas del bulto {bultos.Count + 1} son demasiado grandes."); }

            var tarifa = await administracion.CalcularTarifaAsync(zona.Id, command.Modalidad, bulto.PesoKg, volumenM3, ct)
                ?? throw new EnvioNoCotizableException($"El bulto {bultos.Count + 1} no tiene una tarifa aplicable en el cuadro tarifario vigente.");
            if (versionTarifario is not null && versionTarifario.Id != tarifa.VersionTarifarioId)
                throw new DomainException("El cuadro tarifario cambió durante el alta. Volvé a confirmar el envío.");
            versionTarifario = new DatosVersionTarifario(tarifa.VersionTarifarioId, tarifa.VersionTarifarioNumero);
            bultos.Add(new DatosBulto(bulto.PesoKg, bulto.LargoCm, bulto.AnchoCm, bulto.AltoCm, tarifa.Monto));
        }

        var plazo = reglas.Plazos.SingleOrDefault(p => p.Modalidad == command.Modalidad)?.Plazo;
        return new CotizacionEnvio(zona.Id, reglas.VersionId, versionTarifario, bultos, plazo);
    }

    private (Guid OperadorId, Guid ComercioId) Inquilino()
    {
        var operadorId = tenant.OperadorId
            ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        var comercioId = tenant.ComercioId
            ?? throw new InvalidOperationException("La sesión no tiene un comercio.");
        return (operadorId, comercioId);
    }
}

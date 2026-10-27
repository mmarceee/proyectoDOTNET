using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Application.Features.ReprogramarPorDestinatario;

// CU-61: el destinatario elige otra fecha y franja desde el seguimiento público. Envíos coordina porque la transición es
// suya (decisión H2): pide franjas, zona horaria y reglas a Administración, y a Planificación que saque al envío de su
// ruta (T19). Todo en una sola transacción: si una regla falla, no queda nada a medias. Las reglas las decide Envio.
internal sealed class ReprogramarPorDestinatarioHandler(IEnvioRepository envios, IAdministracionModuleApi administracion,
    IPlanificacionModuleApi planificacion, IUnidadDeTrabajo unidad, TimeProvider reloj)
{
    // Decisión H3: se ofrecen los próximos 7 días, empezando hoy.
    public const int DiasOfrecidos = 7;

    // Paso 2: las fechas y franjas de la zona del envío que respetan la anticipación (y, si ya tuvo un intento, el plazo
    // entre intentos). null si el envío no existe o es de otro operador; vacía si ya no se puede reprogramar (A1).
    public async Task<IReadOnlyList<OpcionReprogramacion>?> OpcionesAsync(Guid envioId, CancellationToken ct)
    {
        if (await envios.ObtenerAsync(envioId, ct) is not { } envio)
        {
            return null;
        }

        if (!Envio.EstadosReprogramablesPorDestinatario.Contains(envio.Estado) || envio.ZonaId is null)
        {
            return [];
        }

        var reglas = await ReglasAsync(envio, ct);
        var zona = await ZonaHorariaAsync(ct);
        var ahora = reloj.GetUtcNow();
        var desde = ahora + reglas.AnticipacionReprogramacion;
        if (envio.Estado == EstadoEnvio.Reprogramado && envio.ReintentoDesde is DateTimeOffset reintento && reintento > desde)
        {
            desde = reintento;
        }

        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, zona).DateTime);
        var opciones = new List<OpcionReprogramacion>();
        for (var dia = 0; dia < DiasOfrecidos; dia++)
        {
            var fecha = hoy.AddDays(dia);
            var recursos = await administracion.ConsultarRecursosPlanificacionAsync(fecha, ct);
            opciones.AddRange(recursos.Franjas
                .Where(f => f.ZonaId == envio.ZonaId && Comienzo(fecha, f.Desde, zona) >= desde)
                .OrderBy(f => f.Desde)
                .Select(f => new OpcionReprogramacion(fecha, f.Id, f.Desde, f.Hasta)));
        }

        return opciones;
    }

    // Pasos 3 a 5. false si el envío no existe o es de otro operador; una regla rota lanza DomainException (400).
    public async Task<bool> ReprogramarAsync(Guid envioId, DateOnly fecha, Guid franjaId, CancellationToken ct)
    {
        var encontrado = false;
        await unidad.EjecutarAsync(async () =>
        {
            if (await envios.ObtenerAsync(envioId, ct) is not { } envio)
            {
                return;
            }

            encontrado = true;

            // A3: la franja tiene que aplicar ese día y ser de la zona del envío.
            var franja = (await administracion.ResolverFranjasAsync([franjaId], fecha, ct)).SingleOrDefault();
            if (franja is null || franja.ZonaId != envio.ZonaId)
            {
                throw new DomainException("Esa franja no está disponible ese día. Elegí otra de las opciones.");
            }

            var reglas = await ReglasAsync(envio, ct);
            var comienzo = Comienzo(fecha, franja.Desde, await ZonaHorariaAsync(ct));

            // T19: la ruta es de Planificación. Si ya se despachó, el envío salió a reparto (A1).
            if (envio.Estado == EstadoEnvio.AsignadoARuta
                && !await planificacion.QuitarPorReprogramacionAsync(envio.Id, envio.Numero, ct))
            {
                throw new DomainException("Tu envío ya salió a reparto: no se puede reprogramar.");
            }

            var ahora = reloj.GetUtcNow();
            envio.ReprogramarPorDestinatario(fecha, franja.Id, comienzo, reglas.AnticipacionReprogramacion,
                reglas.MaxIntentos, ahora);

            // El aviso al comercio y la invalidación del seguimiento salen por el Outbox, en la misma transacción.
            envios.RegistrarReprogramacion(envio, ahora, OrigenEvento.SeguimientoPublico);
            await envios.GuardarCambiosAsync(ct);
        }, ct);

        return encontrado;
    }

    // Las reglas con que se admitió el envío; si es anterior a CU-06, las vigentes (como el resto de Envíos).
    private async Task<ReglasOperativas> ReglasAsync(Envio envio, CancellationToken ct)
    {
        return (envio.VersionReglasId is Guid version ? await administracion.ReglasDeEntregaAsync(version, ct) : null)
            ?? await administracion.ReglasDeEntregaVigentesAsync(ct)
            ?? throw new DomainException("El operador no tiene reglas operativas vigentes.");
    }

    private async Task<TimeZoneInfo> ZonaHorariaAsync(CancellationToken ct) =>
        TimeZoneInfo.FindSystemTimeZoneById(await administracion.ObtenerZonaHorariaActualAsync(ct));

    // Cuándo empieza la franja ese día, en la zona horaria del operador (como en CU-19).
    private static DateTimeOffset Comienzo(DateOnly fecha, TimeOnly hora, TimeZoneInfo zona)
    {
        var local = fecha.ToDateTime(hora);
        return new DateTimeOffset(local, zona.GetUtcOffset(local));
    }
}

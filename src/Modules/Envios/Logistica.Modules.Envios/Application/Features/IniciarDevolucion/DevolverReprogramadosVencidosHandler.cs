using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Application.Features.IniciarDevolucion;

// CU-70, paso 2 (T14): el cierre diario inicia la devolución de los envíos que siguen Reprogramado después del plazo de
// devolución de su versión de reglas, contado desde que entraron en ese estado. Un envío con una fecha de entrega
// posterior a hoy no vence: el despachador ya le dio un nuevo intento. Cada devolución es la de CU-20, con origen
// Sistema, y se guarda por separado: si una falla, las demás siguen y la próxima vuelta la reintenta. Repetirlo no
// devuelve dos veces, porque el envío ya no está Reprogramado (A1).
internal sealed class DevolverReprogramadosVencidosHandler(IEnvioRepository envios, IAdministracionModuleApi administracion,
    IniciarDevolucionHandler devolucion, TimeProvider reloj)
{
    public const string Motivo = "Venció el plazo de devolución sin poder reasignarse.";

    public async Task<int> HandleAsync(DateOnly hoy, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var plazos = new Dictionary<Guid, int?>();
        var devueltos = 0;
        foreach (var envio in await envios.ListarReprogramadosAsync(ct))
        {
            if (envio.FechaEntregaProgramada > hoy) continue;
            if (await PlazoDiasAsync(envio.VersionReglasId, plazos, ct) is not int dias) continue;
            if (!VencioElPlazo(envio, dias, ahora)) continue;

            try
            {
                if (await devolucion.HandleAsync(
                        new IniciarDevolucionCommand(envio.Id, Motivo, OrigenEvento.Sistema, null), ct))
                    devueltos++;
            }
            catch (DomainException)
            {
                // Otro usuario lo cambió entre la lista y la devolución: ya no está Reprogramado.
            }
        }
        return devueltos;
    }

    // Desde la última vez que entró en Reprogramado (un CU-19 que lo deja Reprogramado no reinicia el plazo).
    internal static bool VencioElPlazo(Envio envio, int dias, DateTimeOffset ahora)
    {
        var desde = envio.Eventos
            .Where(e => e.EstadoNuevo == EstadoEnvio.Reprogramado && e.EstadoAnterior != EstadoEnvio.Reprogramado)
            .Select(e => (DateTimeOffset?)e.OcurridoEn)
            .Max();
        return desde is not null && desde.Value.AddDays(dias) <= ahora;
    }

    // Los envíos viejos sin versión usan la vigente, como CU-17 y CU-18. Sin reglas, no hay plazo y no se devuelve.
    private async Task<int?> PlazoDiasAsync(Guid? versionId, Dictionary<Guid, int?> plazos, CancellationToken ct)
    {
        var clave = versionId ?? Guid.Empty;
        if (!plazos.TryGetValue(clave, out var dias))
        {
            var reglas = versionId is Guid id
                ? await administracion.ReglasDeEntregaAsync(id, ct)
                : await administracion.ReglasDeEntregaVigentesAsync(ct);
            plazos[clave] = dias = reglas?.PlazoDevolucionDias;
        }
        return dias;
    }
}

using Logistica.Modules.Ejecucion.Contracts;
using Logistica.Modules.Ejecucion.Contracts.Results;
using Logistica.Modules.Ejecucion.Domain.Rendiciones;
using Logistica.SharedKernel;

namespace Logistica.Modules.Ejecucion.Application;

internal sealed class EjecucionModuleApi(IRendicionRepository rendiciones, TimeProvider reloj,
    Domain.Posiciones.IPosicionRepository posiciones,
    Microsoft.Extensions.Options.IOptions<Features.ReportarPosicion.RetencionPosiciones> retencion,
    Domain.Operaciones.IOperacionRepository operaciones) : IEjecucionModuleApi
{
    public async Task<IReadOnlyList<ConflictoPendiente>> ListarConflictosPendientesAsync(CancellationToken ct)
    {
        return (await operaciones.ConflictosPendientesAsync(ct))
            .Select(o => new ConflictoPendiente(o.Id, o.RutaId, o.EnvioId, o.RepartidorId, o.Tipo, o.Resultado.ToString(),
                o.Motivo, o.OcurridoEn, o.RecibidoEn))
            .ToArray();
    }

    public async Task<bool> MarcarConflictoRevisadoAsync(Guid operacionId, CancellationToken ct)
    {
        if (await operaciones.ObtenerParaRevisarAsync(operacionId, ct) is not { } operacion)
        {
            return false;
        }

        operacion.MarcarRevisada();
        await operaciones.GuardarCambiosAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<PosicionVehiculoResult>> ConsultarUltimasPosicionesAsync(IReadOnlyCollection<Guid> vehiculos, CancellationToken ct)
    {
        if (vehiculos.Count > 500 || vehiculos.Contains(Guid.Empty)) throw new DomainException("La consulta admite hasta 500 vehículos válidos.");
        if (vehiculos.Count == 0) return [];
        return (await posiciones.UltimasAsync(vehiculos, reloj.GetUtcNow().AddDays(-retencion.Value.Dias), ct))
            .Select(Mapping.PosicionMapper.Mapear).ToArray();
    }
    public Task<bool> TieneActividadRegistradaAsync(Guid rutaId, CancellationToken ct)
        => rendiciones.TieneActividadRegistradaAsync(rutaId, ct);
    public async Task<RendicionPendiente?> ConsultarRendicionAsync(Guid rutaId, CancellationToken ct)
    {
        var r = await rendiciones.ObtenerAsync(rutaId, ct);
        return r is null ? null : Mapear(r);
    }
    public async Task<IReadOnlyList<RendicionPendiente>> ListarRendicionesPendientesAsync(int pagina, CancellationToken ct)
    {
        if (pagina is < 1 or > 10000) throw new DomainException("La página no es válida.");
        return (await rendiciones.PendientesAsync(pagina, ct)).Select(Mapear).ToArray();
    }
    // CU-55: la regla de cada línea está en Rendicion.Confirmar; acá sólo se guarda.
    public async Task<RendicionPendiente?> ConfirmarRendicionAsync(Guid rutaId, IReadOnlyCollection<Guid> bultosRecibidos,
        CancellationToken ct)
    {
        if (await rendiciones.ObtenerAsync(rutaId, ct) is not { } r) return null;
        r.Confirmar(bultosRecibidos.ToHashSet(), reloj.GetUtcNow());
        await rendiciones.GuardarAsync(ct);
        return Mapear(r);
    }
    private static RendicionPendiente Mapear(Rendicion r) => new(r.Id, r.RutaId, r.RepartidorId, r.IniciadaEn,
        r.FinalizadaEn, r.Observaciones, r.Lineas.Select(l => new LineaRendicionPendiente(l.EnvioId, l.BultoId,
            l.Situacion, l.Declarada, l.Observacion, l.Recibida, l.EstadoFinal?.ToString(), l.DebeVolver)).ToArray());
}

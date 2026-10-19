using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Ejecucion.Domain.Rendiciones;
using Logistica.Modules.Envios.Contracts;

namespace Logistica.Modules.Ejecucion.Application.Features.IniciarRendicion;

internal sealed class PrepararRendicion(IRendicionRepository rendiciones, IEnviosModuleApi envios)
{
    public async Task<PreparacionRendicionResponse> ConsultarAsync(Guid rutaId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var evidencia = await rendiciones.EvidenciaAsync(rutaId, ct);
        var lista = await envios.ConsultarCargaAsync(ids, ct);
        if (lista.Count != ids.Count) throw new DescargarHojaDeRuta.HojaIncompletaException();
        var bultos = lista.OrderBy(e => e.Numero).SelectMany(e => e.Bultos.OrderBy(b => b.Codigo).Select(b =>
        {
            var situacion = e.Estado switch
            {
                "Entregado" => "EntregadoEnCalle",
                "Devuelto" => "Resuelto",
                "Extraviado" => "Extraviado",
                "EnTransito" => "NoIntentado",
                "Reprogramado" or "EnDevolucion" when evidencia.Intentados.Contains(e.Id) => "IntentoFallido",
                "AsignadoARuta" when evidencia.BultosEscaneados.Contains(b.Id) => "CargaParcial",
                "AsignadoARuta" => "NoCargado",
                _ => "Revisar"
            };
            return new BultoRendicionResponse(e.Id, e.Numero, b.Id, b.Codigo, situacion,
                situacion is "NoIntentado" or "IntentoFallido" or "CargaParcial");
        })).ToArray();
        var iniciada = await rendiciones.ObtenerAsync(rutaId, ct);
        return new(bultos, iniciada is null ? null : Mapear(iniciada));
    }
    internal static RendicionResponse Mapear(Rendicion r) => new(r.Id, r.RutaId, r.IniciadaEn, r.FinalizadaEn,
        r.Observaciones, r.Lineas.OrderBy(l => l.BultoId).Select(l => new LineaRendicionResponse(l.EnvioId,
            l.BultoId, l.Situacion, l.Declarada, l.Observacion, l.Recibida, l.EstadoFinal?.ToString())).ToArray());
}

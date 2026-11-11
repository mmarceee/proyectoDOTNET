using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Ejecucion.Domain.Rendiciones;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Results;

namespace Logistica.Modules.Ejecucion.Application.Features.IniciarRendicion;

internal sealed class PrepararRendicion(IRendicionRepository rendiciones, IEnviosModuleApi envios)
{
    public async Task<PreparacionRendicionResponse> ConsultarAsync(Guid rutaId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var evidencia = await rendiciones.EvidenciaAsync(rutaId, ct);
        var lista = await envios.ConsultarParaRendicionAsync(ids, ct);
        if (lista.Count != ids.Count) throw new DescargarHojaDeRuta.HojaIncompletaException();
        var bultos = lista.OrderBy(e => e.Numero).SelectMany(e => DeLaRuta(e, evidencia).OrderBy(b => b.Codigo).Select(b =>
        {
            // RF 31: la situación sale del estado de cada bulto, no del envío: en una visita parcial el envío queda
            // Reprogramado aunque uno de sus bultos se haya entregado en la calle.
            var situacion = b.Estado switch
            {
                "Entregado" => "EntregadoEnCalle",
                "Devuelto" => "Resuelto",
                "Extraviado" => "Extraviado",
                "EnTransito" => "NoIntentado",
                "Reprogramado" or "EnDevolucion" when evidencia.Intentados.Contains(e.Id) => "IntentoFallido",
                "AsignadoARuta" when !evidencia.CargaPorBulto && evidencia.BultosEscaneados.Contains(b.Id) => "CargaParcial",
                "AsignadoARuta" => "NoCargado",
                "EnDeposito" => "NoCargado",
                _ => "Revisar"
            };
            return new BultoRendicionResponse(e.Id, e.Numero, b.Id, b.Codigo, situacion,
                situacion is "NoIntentado" or "IntentoFallido" or "CargaParcial");
        })).ToArray();
        var iniciada = await rendiciones.ObtenerAsync(rutaId, ct);
        return new(bultos, iniciada is null ? null : Mapear(iniciada));
    }

    // Los bultos que salieron en esta ruta: los escaneados en su carga, más los que siguen asignados sin cargar (una carga
    // incompleta). Un bulto entregado en una visita anterior no se escaneó en esta carga y queda afuera. Sin escaneos del
    // envío en la ruta (no se cargó, o datos anteriores al escaneo), los que acompañan al envío: los de su mismo estado.
    private static IEnumerable<BultoParaRendicion> DeLaRuta(EnvioParaRendicion envio, EvidenciaRuta evidencia) =>
        envio.Bultos.Any(b => evidencia.BultosEscaneados.Contains(b.Id))
            ? envio.Bultos.Where(b => evidencia.BultosEscaneados.Contains(b.Id) || b.Estado == "AsignadoARuta")
            : envio.Bultos.Where(b => b.Estado == envio.Estado);

    internal static RendicionResponse Mapear(Rendicion r) => new(r.Id, r.RutaId, r.IniciadaEn, r.FinalizadaEn,
        r.Observaciones, r.Lineas.OrderBy(l => l.BultoId).Select(l => new LineaRendicionResponse(l.EnvioId,
            l.BultoId, l.Situacion, l.Declarada, l.Observacion, l.Recibida, l.EstadoFinal?.ToString())).ToArray());
}

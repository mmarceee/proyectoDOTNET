using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Ejecucion.Domain.Rendiciones;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Planificacion.Contracts;

namespace Logistica.Modules.Ejecucion.Application.Features.IniciarRendicion;

internal sealed class PrepararRendicion(IRendicionRepository rendiciones, IEnviosModuleApi envios, IPlanificacionModuleApi reservas)
{
    public async Task<PreparacionRendicionResponse> ConsultarAsync(Guid rutaId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var evidencia = await rendiciones.EvidenciaAsync(rutaId, ct);
        var lista = await envios.ConsultarParaRendicionAsync(ids, ct);
        if (lista.Count != ids.Count) throw new DescargarHojaDeRuta.HojaIncompletaException();
        var manifiesto = (await reservas.ConsultarReservasRutaAsync(rutaId, ct)).GroupBy(r => r.BultoId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CerradaEn == null).ThenByDescending(r => r.AbiertaEn).First());
        var bultos = lista.OrderBy(e => e.Numero).SelectMany(e => e.Bultos.Where(b => manifiesto.ContainsKey(b.Id)).OrderBy(b => b.Codigo).Select(b =>
        {
            // RF 31: la situación sale del estado de cada bulto, no del envío: en una visita parcial el envío queda
            // Reprogramado aunque uno de sus bultos se haya entregado en la calle.
            var reserva = manifiesto[b.Id];
            var situacion = reserva.CerradaEn is not null ? reserva.CausaCierre == "Entregado" ? "EntregadoEnCalle" : "Resuelto" : b.Estado switch
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

    internal static RendicionResponse Mapear(Rendicion r) => new(r.Id, r.RutaId, r.IniciadaEn, r.FinalizadaEn,
        r.Observaciones, r.Lineas.OrderBy(l => l.BultoId).Select(l => new LineaRendicionResponse(l.EnvioId,
            l.BultoId, l.Situacion, l.Declarada, l.Observacion, l.Recibida, l.EstadoFinal?.ToString())).ToArray());
}

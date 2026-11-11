using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Application;

internal sealed partial class EnviosModuleApi
{
    public async Task<IReadOnlyList<CustodiaBulto>> ConsultarCustodiaAsync(IReadOnlyCollection<Guid> envioIds, CancellationToken ct)
        => (await envios.ObtenerParaPlanificacionAsync(envioIds, ct)).SelectMany(e => e.Bultos.Select(b =>
            new CustodiaBulto(b.Id, e.Id, b.Codigo, b.Estado.ToString(), b.Custodia.ToString(),
                b.CustodiaRutaId, b.RevisionCustodia, b.CustodiaConfirmadaEn))).ToArray();

    public async Task ConfirmarCustodiaAsync(ConciliacionBultos c, CancellationToken ct)
    {
        var ids = c.Bultos.Select(b => b.BultoId).ToArray();
        var lista = await envios.ObtenerPorBultosAsync(ids, ct);
        var bultos = lista.SelectMany(e => e.Bultos).Where(b => ids.Contains(b.Id)).ToArray();
        if (ids.Length == 0 || ids.Distinct().Count() != ids.Length || bultos.Length != ids.Length
            || bultos.Any(b => b.RevisionCustodia != c.Bultos.Single(x => x.BultoId == b.Id).RevisionEsperada
                || b.Estado is not (EstadoEnvio.EnDeposito or EstadoEnvio.AsignadoARuta or EstadoEnvio.EnTransito or EstadoEnvio.Reprogramado or EstadoEnvio.EnDevolucion)
                || b.CustodiaRutaId is Guid ruta && ruta != c.RutaId))
            throw new DomainException("La custodia cambió o algún bulto no admite conciliación. Recargá antes de confirmar.");
        foreach (var b in bultos)
            b.ConfirmarCustodia(c.EnDeposito ? UbicacionCustodia.Deposito : UbicacionCustodia.Desconocida,
                null, c.ResponsableId, c.OperacionId, reloj.GetUtcNow(), c.Motivo);
        // Recuperación de datos históricos sin pertenencia inequívoca: la confirmación física autoriza T4/T8.
        if (c.EnDeposito && c.RutaId is null)
            foreach (var e in lista)
            {
                var mover = e.Bultos.Where(b => ids.Contains(b.Id) && b.Estado is EstadoEnvio.AsignadoARuta or EstadoEnvio.EnTransito)
                    .Select(b => b.Id).ToArray();
                if (mover.Length > 0) e.CambiarBultosDeRuta(mover, EstadoEnvio.EnDeposito, c.ResponsableId, reloj.GetUtcNow());
            }
        foreach (var e in lista)
            eventos.Registrar(CustodiaBultosConciliada.Tipo, new CustodiaBultosConciliada(e.Id,
                e.Bultos.Where(b => ids.Contains(b.Id)).Select(b => b.Id).ToArray(), c.OperacionId, c.RutaId,
                c.EnDeposito, c.ResponsableId, reloj.GetUtcNow()), reloj.GetUtcNow());
        await envios.GuardarCambiosAsync(ct);
    }

    public async Task RegistrarCustodiaCargaAsync(Guid rutaId, IReadOnlyCollection<Guid> ids, Guid operacionId,
        Guid? actor, DateTimeOffset ocurridoEn, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        var propias = (await reservas.ConsultarReservasRutaAsync(rutaId, ct)).Where(r => r.CerradaEn == null).Select(r => r.BultoId).ToHashSet();
        var bultos = (await envios.ObtenerPorBultosAsync(ids, ct)).SelectMany(e => e.Bultos).Where(b => ids.Contains(b.Id)).ToArray();
        if (ids.Distinct().Count() != ids.Count || bultos.Length != ids.Count
            || bultos.Any(b => !propias.Contains(b.Id) || b.Estado != EstadoEnvio.EnTransito
                || b.Custodia != UbicacionCustodia.Deposito))
            throw new DomainException("La carga requiere reserva propia y presencia confirmada en depósito.");
        foreach (var b in bultos) b.ConfirmarCustodia(UbicacionCustodia.Repartidor, rutaId, actor,
            operacionId, ocurridoEn, "Carga física confirmada");
        await envios.GuardarCambiosAsync(ct);
    }

    public async Task RegistrarCustodiaVisitaAsync(Guid rutaId, IReadOnlyCollection<Guid> ids, Guid operacionId,
        Guid? actor, DateTimeOffset ocurridoEn, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        var bultos = (await envios.ObtenerPorBultosAsync(ids, ct)).SelectMany(e => e.Bultos).Where(b => ids.Contains(b.Id)).ToArray();
        if (bultos.Length != ids.Count || bultos.Any(b => b.Estado != EstadoEnvio.Entregado
            || b.Custodia != UbicacionCustodia.Repartidor || b.CustodiaRutaId != rutaId))
            throw new DomainException("La entrega no corresponde a la custodia de esta ruta.");
        foreach (var b in bultos) b.ConfirmarCustodia(UbicacionCustodia.Destinatario, null, actor,
            operacionId, ocurridoEn, "Entrega confirmada en visita");
        await envios.GuardarCambiosAsync(ct);
        await reservas.CerrarReservasBultosAsync(rutaId, ids, "Entregado", ct);
    }

    public async Task ReintegrarBultosAsync(Guid rutaId, IReadOnlyCollection<Guid> ids, Guid? actor, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        var lista = await envios.ObtenerPorBultosAsync(ids, ct);
        var bultos = lista.SelectMany(e => e.Bultos).Where(b => ids.Contains(b.Id)).ToArray();
        if (bultos.Length != ids.Count || bultos.Any(b => b.Custodia != UbicacionCustodia.Deposito
            || b.Estado is not (EstadoEnvio.AsignadoARuta or EstadoEnvio.EnTransito or EstadoEnvio.Reprogramado or EstadoEnvio.EnDevolucion)))
            throw new DomainException("Confirmá la presencia física de los bultos antes de liberarlos.");
        foreach (var e in lista)
        {
            var sinIntento = e.Bultos.Any(b => ids.Contains(b.Id) && b.Estado == EstadoEnvio.EnTransito);
            var sinCarga = e.Bultos.Any(b => ids.Contains(b.Id) && b.Estado == EstadoEnvio.AsignadoARuta);
            var mover = e.Bultos.Where(b => ids.Contains(b.Id)
                && b.Estado is EstadoEnvio.AsignadoARuta or EstadoEnvio.EnTransito).Select(b => b.Id).ToArray();
            if (mover.Length > 0) e.CambiarBultosDeRuta(mover, EstadoEnvio.EnDeposito, actor, reloj.GetUtcNow());
            eventos.Registrar(PlanificacionBultosActualizada.Tipo,
                new PlanificacionBultosActualizada(e.Id, rutaId, e.Bultos.Where(b => ids.Contains(b.Id)).Select(b => b.Id).ToArray(),
                    "Retorno", reloj.GetUtcNow()), reloj.GetUtcNow());
            if (e.Bultos.Where(b => !Envio.EstadosTerminales.Contains(b.Estado)).All(b => b.Estado == EstadoEnvio.EnDeposito))
            {
                if (sinCarga) envios.RegistrarDesasignacion(e.Id, rutaId, actor, reloj.GetUtcNow());
                if (sinIntento) eventos.Registrar(EnvioReintegradoADeposito.Tipo,
                    new EnvioReintegradoADeposito(e.Id, e.Numero, reloj.GetUtcNow(), actor), reloj.GetUtcNow());
            }
        }
        await envios.GuardarCambiosAsync(ct);
        await reservas.CerrarReservasBultosAsync(rutaId, ids, "RetornoConfirmado", ct);
    }
}

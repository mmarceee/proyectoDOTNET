using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.Modules.Deposito.Contracts;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Planificacion.Domain.Rutas;

namespace Logistica.Modules.Planificacion.Application.Services;

internal sealed class PreparadorEnviosRuta(IDepositoModuleApi deposito, IAdministracionModuleApi administracion)
{
    public async Task<IReadOnlyList<EnvioRuta>> PrepararAsync(IReadOnlyList<EnvioPlanificable> lista, DateOnly fecha,
        RecursosPlanificacion recursos, CancellationToken ct)
    {
        var medidas = (await deposito.ObtenerMedidasRecepcionAsync(lista.SelectMany(e => e.Bultos).Select(b => b.Id).ToArray(), ct)).ToDictionary(m => m.BultoId);
        var franjas = await administracion.ResolverFranjasAsync(lista.Where(e => e.FranjaHorariaId != null).Select(e => e.FranjaHorariaId!.Value).ToArray(), fecha, ct);
        return lista.Select(e =>
        {
            var zona = e.ZonaId ?? recursos.Zonas.SingleOrDefault(z => z.CodigosPostales.Contains(e.CodigoPostal))?.Id;
            var f = franjas.SingleOrDefault(f => f.ReferenciaId == e.FranjaHorariaId);
            var carga = e.Bultos.Select(b =>
            {
                medidas.TryGetValue(b.Id, out var m);
                return new CargaBulto(e.Id, b.Id, m?.PesoKg ?? b.PesoKg, m?.LargoCm ?? b.LargoCm,
                    m?.AnchoCm ?? b.AnchoCm, m?.AltoCm ?? b.AltoCm);
            }).ToList();
            return new EnvioRuta(e.Id, e.Numero, e.FechaEntregaProgramada, zona,
                f is null ? null : new FranjaRuta(f.Id, f.ZonaId, f.Desde, f.Hasta, f.Dias), e.FranjaHorariaId is not null, carga);
        }).ToList();
    }
}

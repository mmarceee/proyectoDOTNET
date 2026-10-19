using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Ejecucion.Domain.Rendiciones;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Ejecucion.Application.Features.ConsultarRendicion;

internal sealed class ConsultarRendicionHandler(IConsultaConsistente lectura, IContextoHojaRuta contexto,
    IPlanificacionModuleApi rutas, IRendicionRepository rendiciones)
{
    public Task<RendicionResponse?> HandleAsync(Guid usuario, Guid repartidor, Guid ruta, CancellationToken ct)
        => lectura.LeerAsync(async () =>
        {
            if (await contexto.ConsultarAsync(usuario, repartidor, null, ct) is null)
                throw new DescargarHojaDeRuta.VinculoRepartidorInvalidoException();
            if (await rutas.ConsultarCargaAsync(ruta, repartidor, ct) is null) return null;
            var r = await rendiciones.ObtenerAsync(ruta, ct);
            return r is null ? null : IniciarRendicion.PrepararRendicion.Mapear(r);
        }, ct);
}

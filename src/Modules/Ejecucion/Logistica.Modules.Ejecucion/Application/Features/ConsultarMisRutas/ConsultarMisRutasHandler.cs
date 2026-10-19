using Logistica.Http.Contracts.Repartidor;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Ejecucion.Application.Features.DescargarHojaDeRuta;
using Logistica.Modules.Ejecucion.Domain.Rendiciones;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Ejecucion.Application.Features.ConsultarMisRutas;

internal sealed class ConsultarMisRutasHandler(IConsultaConsistente lectura, IContextoHojaRuta administracion,
    IPlanificacionModuleApi planificacion, IEnviosModuleApi envios, IRendicionRepository rendiciones, TimeProvider reloj)
{
    public Task<MisRutasResponse> HandleAsync(Guid usuario, Guid repartidor, int pagina, CancellationToken ct)
        => lectura.LeerAsync(async () =>
        {
            var contexto = await administracion.ConsultarAsync(usuario, repartidor, null, ct)
                ?? throw new VinculoRepartidorInvalidoException();
            var fecha = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(),
                TimeZoneInfo.FindSystemTimeZoneById(contexto.ZonaHoraria)).DateTime);
            var rutas = await planificacion.ListarDelRepartidorAsync(repartidor, pagina, ct);
            var lista = await envios.ConsultarCargaAsync(rutas.Where(r => r.Estado != "Finalizada")
                .SelectMany(r => r.Envios).Distinct().ToArray(), ct);
            var pendientes = lista.Where(e => e.Estado == "AsignadoARuta").Select(e => e.Id).ToHashSet();
            var tarjetas = new List<TarjetaRutaResponse>();
            foreach (var r in rutas)
            {
                var recursos = await administracion.ConsultarAsync(usuario, repartidor, r.VehiculoId, ct);
                var rendicion = await rendiciones.ObtenerAsync(r.Id, ct);
                tarjetas.Add(new(r.Id, r.Fecha, r.Estado, recursos?.Matricula, r.Paradas, r.Envios.Count,
                    r.Envios.Any(pendientes.Contains), rendicion is not null, rendicion?.FinalizadaEn is not null));
            }
            return new MisRutasResponse(fecha, tarjetas);
        }, ct);
}

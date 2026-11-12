using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;

namespace Logistica.IntegrationTests;

// Preparación explícita para escenarios anteriores que insertan agregados sin pasar por recepción/carga HTTP.
// Sólo se llama cuando el escenario conoce físicamente su manifiesto; nunca se usa como respaldo en producción.
internal static class DatosFisicosRf34
{
    public static async Task AcreditarAsync(EnviosDbContext enviosDb, PlanificacionDbContext plan,
        Ruta ruta, IEnumerable<Envio> envios, DateTimeOffset ahora, IEnumerable<Guid>? seleccion = null)
    {
        var elegidos = seleccion?.ToHashSet();
        foreach (var envio in envios)
        foreach (var bulto in envio.Bultos.Where(b => elegidos is null || elegidos.Contains(b.Id)))
        {
            var reserva = ReservaBulto.Abrir(ruta, envio.Id, bulto.Id, Guid.NewGuid(), null, ahora);
            plan.ReservasBulto.Add(reserva);
            var ubicacion = bulto.Estado switch
            {
                EstadoEnvio.Entregado => UbicacionCustodia.Destinatario,
                EstadoEnvio.Devuelto => UbicacionCustodia.Comercio,
                EstadoEnvio.EnTransito or EstadoEnvio.Reprogramado or EstadoEnvio.EnDevolucion => UbicacionCustodia.Repartidor,
                EstadoEnvio.Extraviado => UbicacionCustodia.Desconocida,
                _ => UbicacionCustodia.Deposito,
            };
            bulto.ConfirmarCustodia(ubicacion, ubicacion == UbicacionCustodia.Repartidor ? ruta.Id : null,
                null, Guid.NewGuid(), ahora, "Hecho físico acreditado por la preparación del escenario");
            if (ubicacion == UbicacionCustodia.Destinatario) reserva.Cerrar("Entregado", ahora);
        }
        await enviosDb.SaveChangesAsync();
        await plan.SaveChangesAsync();
    }
}

using Logistica.Modules.Seguimiento.Application.HechosEnvio;
using Logistica.Modules.Seguimiento.Contracts.Events;
using Logistica.Modules.Seguimiento.Domain.Avisos;

namespace Logistica.Modules.Seguimiento.Application.Features.EntregarAvisos;

// CU-64, pasos 1 a 3: crea una entrega por cada suscripción activa del comercio que recibe el hecho, y deja en el
// outbox un AvisoPendiente por entrega. No llama a ningún receptor: lo hace EntregarAvisoHandler, una entrega por vez.
internal sealed class PrepararAvisosHandler(EnviosSeguidos envios, ISuscripcionesAvisoRepository suscripciones,
    IEntregasAvisoRepository entregas, IIntegrationEventPublisher eventos)
{
    public async Task HandleAsync(HechoEnvio hecho, CancellationToken ct)
    {
        var envio = await envios.ObtenerOCrearAsync(hecho, ct);
        var tipo = ClasificadorAvisos.Clasificar(hecho.EstadoEnvio, hecho.IntentoFallido);
        foreach (var suscripcion in await suscripciones.ListarActivasAsync(envio.ComercioId, ct))
        {
            if (!suscripcion.Recibe(tipo) || await entregas.ExisteAsync(hecho.EventoId, suscripcion.Id, ct)) continue;
            var entrega = EntregaAviso.Crear(suscripcion, hecho.EventoId, envio, hecho.EstadoEnvio, tipo, hecho.FechaEvento);
            entregas.Agregar(entrega);
            eventos.Registrar(AvisoPendiente.Tipo, new AvisoPendiente(entrega.Id), hecho.FechaEvento);
        }
        await entregas.GuardarAsync(ct);
    }
}

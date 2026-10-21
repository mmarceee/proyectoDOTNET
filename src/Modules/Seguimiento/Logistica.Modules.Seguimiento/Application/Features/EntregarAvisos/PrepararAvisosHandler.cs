using Logistica.Modules.Seguimiento.Contracts.Events;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Application.Features.EntregarAvisos;

// Un hecho de un envío, leído de un evento de Envíos. EsAlta: es EnvioAdmitido, el único con el comercio en el sobre.
internal sealed record HechoEnvio(Guid EventoId, Guid EnvioId, string? Numero, string EstadoEnvio, bool IntentoFallido,
    DateTimeOffset FechaEvento, bool EsAlta);

// El alta del envío todavía no llegó: el orden entre colas no está garantizado (ADR-0003, sección 2.4), así que
// el mensaje se reintenta con la espera creciente hasta que llegue.
internal sealed class EnvioNoRegistradoException(Guid envioId)
    : Exception($"Seguimiento todavía no registró el envío {envioId}.");

// CU-64, pasos 1 a 3: crea una entrega por cada suscripción activa del comercio que recibe el hecho, y deja en el
// outbox un AvisoPendiente por entrega. No llama a ningún receptor: lo hace EntregarAvisoHandler, una entrega por vez.
internal sealed class PrepararAvisosHandler(IEnviosSeguidosRepository envios, ISuscripcionesAvisoRepository suscripciones,
    IEntregasAvisoRepository entregas, IIntegrationEventPublisher eventos, ICurrentTenant tenant)
{
    public async Task HandleAsync(HechoEnvio hecho, CancellationToken ct)
    {
        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("El mensaje no tiene operador.");
        var envio = await envios.ObtenerAsync(hecho.EnvioId, ct);
        if (envio is null)
        {
            if (!hecho.EsAlta) throw new EnvioNoRegistradoException(hecho.EnvioId);
            // El comercio sale del sobre, nunca del contenido del evento (ADR-0003, sección 2.2).
            var comercioId = tenant.ComercioId
                ?? throw new InvalidDataException("El alta del envío no trae el comercio en el sobre.");
            envio = EnvioSeguido.Crear(operadorId, comercioId, hecho.EnvioId,
                hecho.Numero ?? throw new InvalidDataException("El alta del envío no trae el número."));
            envios.Agregar(envio);
        }

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

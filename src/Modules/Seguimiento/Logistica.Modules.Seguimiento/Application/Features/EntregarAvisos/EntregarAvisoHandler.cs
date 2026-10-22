using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Domain.Avisos;

namespace Logistica.Modules.Seguimiento.Application.Features.EntregarAvisos;

// El receptor no respondió 2xx. El intento ya quedó registrado; la excepción hace que la mensajería reintente el
// mensaje con espera creciente o, agotados los reintentos, lo pase a la cola de fallidos (ADR-0003, sección 2.6).
internal sealed class AvisoNoEntregadoException(Guid entregaId, int intento)
    : Exception($"La entrega {entregaId} del aviso falló en el intento {intento}.");

// CU-64, pasos 3 a 6: un intento HTTP firmado de una entrega.
internal sealed class EntregarAvisoHandler(IEntregasAvisoRepository entregas, ISuscripcionesAvisoRepository suscripciones,
    ISecretosAviso secretos, IEmisorAvisos emisor, TimeProvider reloj)
{
    public async Task HandleAsync(Guid entregaId, bool ultimoIntento, CancellationToken ct)
    {
        var entrega = await entregas.ObtenerAsync(entregaId, ct)
            ?? throw new InvalidDataException("La entrega del aviso no existe para el operador del mensaje.");
        if (entrega.Estado == EstadoEntregaAviso.Exitosa) return;

        // Desactivada después de preparar el aviso: ya no recibe nada y la entrega queda como estaba.
        var suscripcion = await suscripciones.ObtenerAsync(entrega.SuscripcionAvisoId, ct);
        if (suscripcion is not { Activa: true }) return;

        // CU-65 o CU-09 la volvieron a encolar.
        if (entrega.Estado == EstadoEntregaAviso.EnColaDeFallidos) entrega.Reintentar();

        // El secreto se descifra en cada intento: una regeneración rige desde el próximo (guía de integración de CU-63).
        var secreto = secretos.Descifrar(suscripcion.SecretoFirma, entrega.OperadorId, entrega.ComercioId);
        var mensaje = new MensajeAviso(entrega.Id, entrega.Tipo.ToString(), entrega.FechaEvento,
            entrega.NumeroEnvio, entrega.EstadoEnvio, Prueba: false);
        var resultado = await emisor.EnviarAsync(suscripcion.UrlDestino, secreto, mensaje, ct);

        entrega.RegistrarIntento(reloj.GetUtcNow(), resultado.Exitosa, resultado.CodigoRespuesta, resultado.Error, ultimoIntento);
        await entregas.GuardarAsync(ct);
        if (!resultado.Exitosa) throw new AvisoNoEntregadoException(entrega.Id, entrega.Intentos.Count);
    }
}

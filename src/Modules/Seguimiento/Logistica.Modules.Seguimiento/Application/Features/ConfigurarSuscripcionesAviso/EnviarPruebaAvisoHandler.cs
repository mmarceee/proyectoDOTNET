using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Application.Features.ConfigurarSuscripcionesAviso;

internal sealed record ResultadoPruebaAviso(Guid MensajeId, bool Exitosa, int? CodigoRespuesta, string? Error);

internal sealed class EnviarPruebaAvisoHandler(ISuscripcionesAvisoRepository suscripciones, ISecretosAviso secretos,
    IEmisorAvisos emisor, ICurrentTenant tenant, TimeProvider reloj)
{
    public async Task<ResultadoPruebaAviso?> HandleAsync(Guid id, CancellationToken ct)
    {
        var operador = tenant.OperadorId ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        var comercio = tenant.ComercioId ?? throw new InvalidOperationException("La sesión no tiene un comercio.");
        var suscripcion = await suscripciones.ObtenerAsync(id, ct);
        if (suscripcion is null) return null;
        if (!suscripcion.Activa) throw new DomainException("La suscripción está inactiva. No se puede enviar una prueba.");

        var tipo = suscripcion.TiposEvento.OrderBy(t => t).First();
        var estado = tipo switch
        {
            TipoEventoAviso.EntregaFallida => "NoEntregado",
            TipoEventoAviso.Devolucion => "EnDevolucion",
            _ => "Admitido"
        };
        var mensaje = new MensajeAviso(Guid.CreateVersion7(), tipo.ToString(), reloj.GetUtcNow(), "PRUEBA", estado, true);
        var secreto = secretos.Descifrar(suscripcion.SecretoFirma, operador, comercio);
        var resultado = await emisor.EnviarAsync(suscripcion.UrlDestino, secreto, mensaje, ct);
        return new(mensaje.Id, resultado.Exitosa, resultado.CodigoRespuesta, resultado.Error);
    }
}

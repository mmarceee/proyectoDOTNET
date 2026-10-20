using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Application.Features.ConfigurarSuscripcionesAviso;

internal sealed class RegenerarSecretoAvisoHandler(ISuscripcionesAvisoRepository suscripciones,
    ISecretosAviso secretos, ICurrentTenant tenant, IUnidadDeTrabajo unidad)
{
    public async Task<SecretoSuscripcionAviso?> HandleAsync(Guid id, CancellationToken ct)
    {
        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        var comercioId = tenant.ComercioId ?? throw new InvalidOperationException("La sesión no tiene un comercio.");
        SecretoSuscripcionAviso? resultado = null;
        await unidad.EjecutarAsync(async () =>
        {
            var suscripcion = await suscripciones.ObtenerAsync(id, ct);
            if (suscripcion is null) return;
            var secreto = secretos.Generar();
            suscripcion.RegenerarSecreto(secretos.Cifrar(secreto, operadorId, comercioId));
            await suscripciones.GuardarAsync(ct);
            resultado = new(suscripcion.Id, secreto);
        }, ct);
        return resultado;
    }
}

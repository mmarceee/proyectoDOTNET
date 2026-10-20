using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Application.Features.ConfigurarSuscripcionesAviso;

internal sealed class DesactivarSuscripcionAvisoHandler(ISuscripcionesAvisoRepository suscripciones,
    ICurrentTenant tenant, IUnidadDeTrabajo unidad)
{
    public async Task<bool> HandleAsync(Guid id, CancellationToken ct)
    {
        if (tenant.OperadorId is null || tenant.ComercioId is null)
            throw new InvalidOperationException("La desactivación requiere un comercio y un operador en la sesión.");
        var encontrada = false;
        await unidad.EjecutarAsync(async () =>
        {
            var suscripcion = await suscripciones.ObtenerAsync(id, ct);
            if (suscripcion is null) return;
            suscripcion.Desactivar();
            await suscripciones.GuardarAsync(ct);
            encontrada = true;
        }, ct);
        return encontrada;
    }
}

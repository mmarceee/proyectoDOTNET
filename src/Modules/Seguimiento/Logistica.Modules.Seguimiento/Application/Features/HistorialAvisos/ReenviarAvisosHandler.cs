using Logistica.Modules.Seguimiento.Contracts.Events;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Application.Features.HistorialAvisos;

internal interface IReenviosAvisoRepository
{
    Task<EntregaAviso?> BloquearEntregaAsync(Guid id, CancellationToken ct);
    Task<SuscripcionAviso?> BloquearSuscripcionAsync(Guid id, CancellationToken ct);
    Task GuardarAsync(CancellationToken ct);
}

internal sealed class ReenviarAvisosHandler(IReenviosAvisoRepository repositorio, IIntegrationEventPublisher eventos,
    IUnidadDeTrabajo unidad, ICurrentTenant tenant, TimeProvider reloj)
{
    // false: al menos una entrega no existe o no pertenece al inquilino. El lote no se encola parcialmente.
    public async Task<bool> HandleAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (tenant.OperadorId is null || tenant.ComercioId is null)
            throw new InvalidOperationException("El reenvío requiere comercio y operador en la sesión.");
        if (ids is null || ids.Count is < 1 or > 50 || ids.Contains(Guid.Empty))
            throw new DomainException("Seleccioná entre 1 y 50 avisos válidos para reenviar.");
        var solicitado = false;
        await unidad.EjecutarAsync(async () =>
        {
            List<EntregaAviso> entregas = [];
            foreach (var id in ids.Distinct().Order())
            {
                var entrega = await repositorio.BloquearEntregaAsync(id, ct);
                if (entrega is null) return;
                entregas.Add(entrega);
            }
            // Orden estable para evitar que dos lotes adquieran los mismos bloqueos en orden inverso.
            foreach (var id in entregas.Select(e => e.SuscripcionAvisoId).Distinct().Order())
            {
                var suscripcion = await repositorio.BloquearSuscripcionAsync(id, ct);
                if (suscripcion is not { Activa: true })
                    throw new DomainException("Una suscripción está inactiva. No se solicitó ningún reenvío.");
            }
            if (entregas.Any(e => e.Estado != EstadoEntregaAviso.EnColaDeFallidos))
                throw new DomainException("Sólo se reenvían avisos en cola de fallidos. Actualizá el historial; no se solicitó ningún reenvío.");
            foreach (var entrega in entregas)
            {
                entrega.Reintentar();
                eventos.Registrar(AvisoPendiente.Tipo, new AvisoPendiente(entrega.Id), reloj.GetUtcNow());
            }
            await repositorio.GuardarAsync(ct);
            solicitado = true;
        }, ct);
        return solicitado;
    }
}

using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.GestionarMensajesFallidos;

// CU-09: el administrador consulta los mensajes fallidos de su operador y los reintenta o los descarta. La tabla y la
// publicación son técnicas (BuildingBlocks, ADR-0003, sección 2.6); acá quedan las reglas del caso de uso.
internal sealed class MensajesFallidosHandler(IMensajesFallidos fallidos)
{
    public Task<IReadOnlyList<MensajeFallidoResumen>> ListarAsync(bool incluirResueltos, CancellationToken ct) =>
        fallidos.ListarAsync(incluirResueltos, ct);

    public Task<MensajeFallidoDetalle?> ObtenerAsync(Guid id, CancellationToken ct) => fallidos.ObtenerAsync(id, ct);

    // Paso 4 y A2: uno o varios a la vez.
    public Task<int> ReintentarAsync(IReadOnlyCollection<Guid> ids, string usuario, CancellationToken ct)
    {
        if (ids.Count == 0) throw new DomainException("Elegí al menos un mensaje para reintentar.");
        return fallidos.ReintentarAsync(ids.Distinct().ToArray(), usuario, ct);
    }

    // A1: el motivo es obligatorio y queda registrado con quién lo descartó.
    public Task<bool> DescartarAsync(Guid id, string? motivo, string usuario, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(motivo)) throw new DomainException("Indicá el motivo del descarte.");
        return fallidos.DescartarAsync(id, motivo, usuario, ct);
    }
}

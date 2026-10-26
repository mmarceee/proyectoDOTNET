namespace Logistica.SharedKernel;

// CU-09: los mensajes que agotaron sus reintentos, o eran inválidos, y quedaron en la cola de fallidos. Interfaz técnica,
// como IUnidadDeTrabajo: la implementa BuildingBlocks y la usa Administración (ADR-0003, sección 2.6). Todo se filtra
// por el operador de la sesión.
public interface IMensajesFallidos
{
    //Sin resueltos, solo los pendientes. Los mas recientes primero.
    Task<IReadOnlyList<MensajeFallidoResumen>> ListarAsync(bool incluirResueltos, CancellationToken ct);

    Task<MensajeFallidoDetalle?> ObtenerAsync(Guid id, CancellationToken ct);

    // Vuelve a publicar cada mensaje como pendiente en su cola de origen, con el mismo MessageId. Devuelve cuantos reintentó:
    // los que ya no estaban pendientes se ignoran.
    Task<int> ReintentarAsync(IReadOnlyCollection<Guid> ids, string usuario, CancellationToken ct);

    // false si el mensaje no existe para el operador de la sesion.
    Task<bool> DescartarAsync(Guid id, string motivo, string usuario, CancellationToken ct);
}

public sealed record MensajeFallidoResumen(Guid Id, string Tipo, string ColaOrigen, DateTimeOffset OcurridoEn, DateTimeOffset ActualizadoEn, int Intentos, string? UltimoError, string Estado);

public sealed record MensajeFallidoDetalle(MensajeFallidoResumen Resumen, Guid MessageId, string Contenido, IReadOnlyList<string> Historial, string? ResueltoPor, DateTimeOffset? ResueltoEn, string? MotivoDescarte);

namespace Logistica.Modules.Administracion.Contracts;

// CU-01/CU-10: se ejecuta dentro de la unidad de trabajo del alta del envío.
// Conserva el bloqueo hasta confirmar para no competir con la suspensión o baja.
public interface IRelacionComercialActiva
{
    Task ExigirAsync(Guid comercioId, CancellationToken ct);
}

namespace Logistica.SharedKernel;

// Guarda en una sola transacción los cambios que hacen varios módulos en un mismo caso de uso
// (ADR-0001, addendum 3). Si el trabajo falla, no queda guardado nada de ningún módulo.
public interface IUnidadDeTrabajo
{
    Task EjecutarAsync(Func<Task> trabajo, CancellationToken ct);
}

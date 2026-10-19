namespace Logistica.SharedKernel;

// Instantánea de lectura compartida entre módulos, sin dependencia de EF o de HTTP.
public interface IConsultaConsistente
{
    Task<T> LeerAsync<T>(Func<Task<T>> consulta, CancellationToken ct);
}

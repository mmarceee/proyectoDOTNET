namespace Logistica.Modules.Envios.Contracts;

// CU-01: sólo cuenta envíos del operador de la sesión; no expone datos de otros operadores.
public interface IEnviosDeComercio
{
    Task<int> ContarPendientesAsync(Guid comercioId, CancellationToken ct);
}

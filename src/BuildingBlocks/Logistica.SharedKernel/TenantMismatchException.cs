namespace Logistica.SharedKernel;

// Se intentó guardar una entidad de otro inquilino; la lanza el interceptor.
public sealed class TenantMismatchException(string message) : Exception(message);

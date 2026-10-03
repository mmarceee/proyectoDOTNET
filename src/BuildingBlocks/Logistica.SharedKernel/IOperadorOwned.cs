namespace Logistica.SharedKernel;

// La entidad pertenece a un operador: la alcanzan el filtro "Tenant" y el interceptor.
public interface IOperadorOwned
{
    Guid OperadorId { get; }
}

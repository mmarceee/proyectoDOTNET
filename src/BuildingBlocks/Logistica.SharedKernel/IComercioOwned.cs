namespace Logistica.SharedKernel;

// La entidad pertenece ademas a un comercio: el comercio solo ve el suyo.
public interface IComercioOwned
{
    Guid ComercioId { get; }
}

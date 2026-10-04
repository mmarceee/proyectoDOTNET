namespace Logistica.SharedKernel;

// Quien hace la operacion actual. La API lo resuelve desde la cookie o el token,
// y el Worker desde los metadatos del mensaje.
public interface ICurrentTenant
{
    // null: no hay inquilino resuelto, y el filtro no devuelve ninguna fila.
    Guid? OperadorId { get; }

    // null: es personal del operador, no un usuario de comercio
    Guid? ComercioId { get; }
}

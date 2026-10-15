namespace Logistica.Modules.Administracion.Contracts;

// Nombres de los claims que la cookie de sesión lleva además de los de Identity (ADR-0002, sección 2.5).
// Los escribe Administración al iniciar sesión y los lee el ICurrentTenant de la Api.
public static class ClaimsDeSesion
{
    public const string OperadorId = "operador_id";
    public const string ComercioId = "comercio_id";

    // Usuario con perfil Repartidor vinculado a su Repartidor: la sincronización verifica que la ruta sea suya
    // (ADR-0004, sección 2.3).
    public const string RepartidorId = "repartidor_id";
}

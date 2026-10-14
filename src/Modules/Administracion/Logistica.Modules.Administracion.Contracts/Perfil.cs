namespace Logistica.Modules.Administracion.Contracts;

// Perfil de un usuario (modelo de dominio, Perfil; CU-02). Cada valor es un rol de ASP.NET Core Identity con el
// mismo nombre, así que otros módulos autorizan con su nombre: por ejemplo, Planificación exige Despachador.
public enum Perfil
{
    Administrador,
    Despachador,
    Repartidor,
    OperarioDeposito,
    UsuarioComercio,
}

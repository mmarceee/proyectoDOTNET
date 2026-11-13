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
    // Administra la plataforma: da de alta los operadores y sus administradores. No pertenece a ningún operador ni
    // comercio, así que no ve datos de ningún inquilino (el filtro "Tenant" falla cerrado sin operador).
    AdministradorGeneral,
}

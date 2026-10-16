namespace Logistica.Modules.Administracion.Contracts;

// Nombres de las políticas de autorización por perfil (letra, sección 6.3; CU-02). Las define Administración, dueña de
// los perfiles, y cada módulo las pone en sus endpoints con RequireAuthorization(Politicas.X).
public static class Politicas
{
    // Usuario de comercio que ya eligió el operador de la sesión (CU-03). Crea y cancela envíos (CU-10, CU-16).
    public const string Comercio = "Comercio";

    // Recibe bultos en el depósito (CU-30).
    public const string OperarioDeposito = "OperarioDeposito";

    // Planifica y ajusta las entregas: por ejemplo, reprograma un envío no entregado (CU-19).
    public const string Despachador = "Despachador";

    // Configura el operador y gestiona usuarios (CU-01 a CU-08).
    public const string Administrador = "Administrador";

    // Repartidor vinculado a su Repartidor (claim repartidor_id): sincroniza lo que registró en la PWA (CU-52).
    public const string Repartidor = "Repartidor";
}

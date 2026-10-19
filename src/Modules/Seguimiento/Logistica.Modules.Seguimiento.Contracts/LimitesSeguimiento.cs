namespace Logistica.Modules.Seguimiento.Contracts;

// CU-60, A3 (letra 6.3): el nombre de la política de límite de tasa del seguimiento público. La registra el host y la
// exige el endpoint; está acá para que los dos usen el mismo nombre.
public static class LimitesSeguimiento
{
    public const string Politica = "seguimiento-publico";
}

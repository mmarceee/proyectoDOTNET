namespace Logistica.Modules.Administracion.Contracts;

public sealed record IdentidadClaveApi(Guid OperadorId, Guid ComercioId, bool EsPrueba);

public interface IClavesApiAutenticacion
{
    Task<IdentidadClaveApi?> IdentificarAsync(string secreto, CancellationToken ct);
    Task<bool> RegistrarUsoAsync(string secreto, CancellationToken ct);
}

public static class AccesoApiPublica
{
    public const string AmbienteClaim = "clave_api_prueba";
}

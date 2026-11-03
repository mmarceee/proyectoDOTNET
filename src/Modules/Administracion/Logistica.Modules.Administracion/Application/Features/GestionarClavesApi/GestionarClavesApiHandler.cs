using System.Security.Cryptography;
using System.Text;
using Logistica.Modules.Administracion.Application.Features.GestionarComercios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Comercios;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.GestionarClavesApi;

internal interface IClavesApiRepository
{
    Task<IReadOnlyList<ClaveApi>> ListarAsync(CancellationToken ct);
    Task<ClaveApi?> ObtenerAsync(Guid id, CancellationToken ct);
    Task<IdentidadClaveApi?> IdentificarPorHashAsync(string hash, CancellationToken ct);
    Task<bool> RegistrarUsoPorHashAsync(string hash, DateTimeOffset ahora, CancellationToken ct);
    void Agregar(ClaveApi clave);
    Task GuardarAsync(CancellationToken ct);
}

internal sealed class GestionarClavesApiHandler(IClavesApiRepository claves, IComerciosRepository comercios,
    ICurrentTenant tenant, IUnidadDeTrabajo unidad, TimeProvider reloj)
{
    public Task<IReadOnlyList<ClaveApi>> ListarAsync(CancellationToken ct) => claves.ListarAsync(ct);

    public async Task<(ClaveApi Clave, string Secreto)> CrearAsync(string nombre, TipoClaveApi tipo, CancellationToken ct)
    {
        var prefijoTipo = tipo == TipoClaveApi.Prueba ? "log_prueba_" : "log_prod_";
        var aleatorio = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var secreto = prefijoTipo + aleatorio;
        var hash = HashClaveApi.Calcular(secreto);
        ClaveApi? clave = null;

        await unidad.EjecutarAsync(async () =>
        {
            var comercioId = tenant.ComercioId
                ?? throw new TenantMismatchException("La sesión no tiene un comercio.");
            var relacion = await comercios.BloquearRelacionAsync(comercioId, ct)
                ?? throw new DomainException("El comercio no tiene una relación con el operador seleccionado.");

            clave = ClaveApi.Crear(relacion, nombre, tipo, hash, secreto[..ClaveApi.LargoPrefijo], reloj.GetUtcNow());
            claves.Agregar(clave);
            await claves.GuardarAsync(ct);
        }, ct);
        return (clave!, secreto);
    }

    public async Task<bool> RevocarAsync(Guid id, CancellationToken ct)
    {
        var clave = await claves.ObtenerAsync(id, ct);
        if (clave is null) return false;
        clave.Revocar(reloj.GetUtcNow());
        await claves.GuardarAsync(ct);
        return true;
    }
}

internal static class HashClaveApi
{
    public static string Calcular(string secreto) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secreto)));
}

using Logistica.Modules.Administracion.Contracts;

namespace Logistica.Modules.Administracion.Application.Features.GestionarClavesApi;

internal sealed class AutenticarClavesApiHandler(IClavesApiRepository claves, TimeProvider reloj)
    : IClavesApiAutenticacion
{
    public Task<IdentidadClaveApi?> IdentificarAsync(string secreto, CancellationToken ct) =>
        claves.IdentificarPorHashAsync(HashClaveApi.Calcular(secreto), ct);

    public Task<bool> RegistrarUsoAsync(string secreto, CancellationToken ct) =>
        claves.RegistrarUsoPorHashAsync(HashClaveApi.Calcular(secreto), reloj.GetUtcNow(), ct);
}

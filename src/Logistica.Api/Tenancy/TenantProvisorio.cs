using Logistica.SharedKernel;

namespace Logistica.Api.Tenancy;

// Contexto provisorio para probar CU-10 y CU-13 sin autenticación.
// La distinción por ruta se reemplazará por los claims de la sesión de Identity.
internal sealed class TenantProvisorio(
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor) : ICurrentTenant
{
    public Guid? OperadorId { get; } = configuration.GetValue<Guid?>("TenantProvisorio:OperadorId");

    public Guid? ComercioId
    {
        get
        {
            var contexto = httpContextAccessor.HttpContext;

            if (contexto is null)
            {
                return null;
            }

            if (contexto.Request.Path.StartsWithSegments("/backoffice") || contexto.Request.Path.StartsWithSegments("/api/planificacion"))
            {
                return null;
            }

            return configuration.GetValue<Guid?>("TenantProvisorio:ComercioId");
        }
    }
}

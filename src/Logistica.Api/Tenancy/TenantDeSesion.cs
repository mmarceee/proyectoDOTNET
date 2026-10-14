using System.Security.Claims;
using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Api.Tenancy;

// El inquilino del request sale de la cookie de sesión (ADR-0002, sección 2.5): operador y comercio como claims,
// escritos por Administración al iniciar sesión. Nunca de un valor enviado por el cliente.
// Transición hasta que todas las pantallas y pruebas inicien sesión: sin sesión, y sólo en Development con
// Identity:PermitirTenantProvisorio, se usa TenantProvisorio. En cualquier otro caso, sin sesión no hay
// inquilino y no se ve nada (falla cerrado, sección 2.1).
internal sealed class TenantDeSesion(
    IHttpContextAccessor http, TenantProvisorio provisorio, IHostEnvironment entorno, IConfiguration configuracion)
    : ICurrentTenant
{
    public Guid? OperadorId => Leer(ClaimsDeSesion.OperadorId, () => provisorio.OperadorId);

    public Guid? ComercioId => Leer(ClaimsDeSesion.ComercioId, () => provisorio.ComercioId);

    private Guid? Leer(string claim, Func<Guid?> valorProvisorio)
    {
        var usuario = http.HttpContext?.User;

        // Con sesión, sólo vale la cookie. Un usuario de comercio que todavía no eligió operador no tiene
        // operador_id, y no ve datos de ningún operador (sección 2.6).
        if (usuario?.Identity?.IsAuthenticated == true)
        {
            return Guid.TryParse(usuario.FindFirstValue(claim), out var id) ? id : null;
        }

        return PermiteProvisorio ? valorProvisorio() : null;
    }

    private bool PermiteProvisorio =>
        entorno.IsDevelopment() && configuracion.GetValue<bool>("Identity:PermitirTenantProvisorio");
}

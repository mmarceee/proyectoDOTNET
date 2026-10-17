using Microsoft.AspNetCore.Authentication;
using Logistica.Modules.Administracion.Contracts;

namespace Logistica.Api.Seguridad;

// Protege también las entradas directas a index.html y recursos del Portal.
// Los permisos por perfil y operador siguen perteneciendo a cada caso de uso.
internal sealed class AccesoAplicacionesMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, IHostEnvironment environment, IConfiguration configuration)
    {
        var portal = http.Request.Path.StartsWithSegments("/portal");
        var repartidor = http.Request.Path.StartsWithSegments("/repartidor");
        var personal = http.Request.Path.StartsWithSegments("/backoffice")
            || http.Request.Path.StartsWithSegments("/api/deposito");
        // El ingreso del Portal tiene que poder abrirse sin sesión.
        if (http.Request.Path.Equals(new PathString("/portal/ingresar"))
            || http.Request.Path.Equals(new PathString("/repartidor/ingresar"))
            // El navegador solicita el manifiesto sin credenciales por defecto.
            // Sólo contiene metadatos públicos; la app y las APIs conservan su protección.
            || http.Request.Path.Equals(new PathString("/repartidor/manifest.webmanifest")))
        {
            await next(http);
            return;
        }
        var protegida = portal || repartidor || personal;
        // Respaldo explícito para pruebas y transición; desactivado en la configuración de la aplicación.
        var respaldo = environment.IsDevelopment()
            && configuration.GetValue<bool>("Identity:PermitirTenantProvisorio");
        if (protegida && http.User.Identity?.IsAuthenticated != true && !respaldo)
        {
            if (portal) http.Response.Redirect("/portal/ingresar");
            else if (repartidor) http.Response.Redirect("/repartidor/ingresar");
            else await http.ChallengeAsync();
            return;
        }
        if (protegida && http.User.Identity?.IsAuthenticated == true)
        {
            var permitido = repartidor
                ? http.User.IsInRole(nameof(Perfil.Repartidor))
                    && !http.User.HasClaim(c => c.Type == ClaimsDeSesion.ComercioId)
                    && Guid.TryParse(http.User.FindFirst(ClaimsDeSesion.OperadorId)?.Value, out _)
                : portal
                ? http.User.IsInRole(nameof(Perfil.UsuarioComercio))
                    && Guid.TryParse(http.User.FindFirst(ClaimsDeSesion.ComercioId)?.Value, out _)
                : !http.User.HasClaim(c => c.Type == ClaimsDeSesion.ComercioId)
                    && (http.User.IsInRole(nameof(Perfil.Administrador))
                        || http.User.IsInRole(nameof(Perfil.Despachador))
                        || http.User.IsInRole(nameof(Perfil.OperarioDeposito)));
            if (!permitido)
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                await http.Response.WriteAsync("Acceso no permitido para el perfil de esta cuenta.");
                return;
            }
        }
        await next(http);
    }
}

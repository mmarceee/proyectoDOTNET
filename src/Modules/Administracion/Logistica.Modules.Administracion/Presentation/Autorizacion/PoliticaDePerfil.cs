using Logistica.Modules.Administracion.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Logistica.Modules.Administracion.Presentation.Autorizacion;

// Requisito de una política: tener el perfil y, si se pide, un operador en la sesión (el comercio lo elige, CU-03).
// ExigeRepartidor: además, el usuario está vinculado a su Repartidor (claim repartidor_id), para saber qué rutas son suyas.
internal sealed record PoliticaDePerfil(Perfil Perfil, bool ExigeOperador = false, bool ExigeRepartidor = false)
    : IAuthorizationRequirement;

// Con sesión, el perfil se exige siempre. Sin sesión, sólo en Development con Identity:PermitirTenantProvisorio, se
// deja pasar como hacen TenantDeSesion y el middleware de acceso: es el respaldo de las pruebas que todavía no inician
// sesión. En cualquier otro caso, sin sesión no se autoriza y la API responde 401.
internal sealed class VerificadorDePerfil(IHostEnvironment entorno, IConfiguration configuracion)
    : AuthorizationHandler<PoliticaDePerfil>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext contexto, PoliticaDePerfil requisito)
    {
        var usuario = contexto.User;

        if (usuario.Identity?.IsAuthenticated != true)
        {
            if (entorno.IsDevelopment() && configuracion.GetValue<bool>("Identity:PermitirTenantProvisorio"))
            {
                contexto.Succeed(requisito);
            }

            return Task.CompletedTask;
        }

        var tieneOperador = Guid.TryParse(usuario.FindFirst(ClaimsDeSesion.OperadorId)?.Value, out _);
        var tieneRepartidor = Guid.TryParse(usuario.FindFirst(ClaimsDeSesion.RepartidorId)?.Value, out _);

        if (usuario.IsInRole(requisito.Perfil.ToString())
            && (!requisito.ExigeOperador || tieneOperador)
            && (!requisito.ExigeRepartidor || tieneRepartidor))
        {
            contexto.Succeed(requisito);
        }

        return Task.CompletedTask;
    }
}

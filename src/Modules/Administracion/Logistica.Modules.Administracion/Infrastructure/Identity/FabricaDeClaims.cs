using System.Security.Claims;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Logistica.Modules.Administracion.Infrastructure.Identity;

// Arma la identidad que viaja en la cookie al iniciar sesión. Identity ya agrega el Id (NameIdentifier), el correo
// y los perfiles como roles; acá se suma el inquilino (ADR-0002, sección 2.5). El personal lleva su operador.
// El usuario de comercio lleva sólo su comercio: el operador lo elige en la sesión (CU-03), y hasta entonces
// no ve datos de ningún operador (sección 2.6).
internal sealed class FabricaDeClaims(
    UserManager<Usuario> usuarios, RoleManager<IdentityRole<Guid>> roles, IOptions<IdentityOptions> opciones,
    VinculoRepartidor vinculos)
    : UserClaimsPrincipalFactory<Usuario, IdentityRole<Guid>>(usuarios, roles, opciones)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Usuario usuario)
    {
        var identidad = await base.GenerateClaimsAsync(usuario);

        if (usuario.OperadorId is Guid operadorId)
        {
            identidad.AddClaim(new Claim(ClaimsDeSesion.OperadorId, operadorId.ToString()));

            // El repartidor lleva además su Repartidor, para que la sincronización sepa qué rutas son suyas.
            if (await UserManager.IsInRoleAsync(usuario, nameof(Perfil.Repartidor))
                && await vinculos.RepartidorDeAsync(operadorId, usuario.Id, CancellationToken.None) is Guid repartidorId)
            {
                identidad.AddClaim(new Claim(ClaimsDeSesion.RepartidorId, repartidorId.ToString()));
            }
        }

        if (usuario.ComercioId is Guid comercioId)
        {
            identidad.AddClaim(new Claim(ClaimsDeSesion.ComercioId, comercioId.ToString()));
        }

        return identidad;
    }
}

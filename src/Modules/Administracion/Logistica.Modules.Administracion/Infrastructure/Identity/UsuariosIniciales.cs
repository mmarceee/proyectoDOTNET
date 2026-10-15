using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace Logistica.Modules.Administracion.Infrastructure.Identity;

// Datos iniciales de Identity (ADR-0002, sección 2.8): los perfiles como roles, y usuarios de demostración para
// entrar como cada operador y como Tienda Demo. Corren al iniciar, después de DatosIniciales; son idempotentes.
// Usuario no tiene filtro de inquilino (sección 2.4): el operador o el comercio se asignan acá, explícitos.
internal sealed class UsuariosIniciales(UserManager<Usuario> usuarios, RoleManager<IdentityRole<Guid>> roles)
{
    // Sólo para la demostración y las pruebas. Cumple la política de contraseñas por defecto de Identity.
    public const string ContrasenaDemo = "Demo-2026!";

    public async Task SembrarAsync()
    {
        foreach (var perfil in Enum.GetNames<Perfil>())
        {
            if (!await roles.RoleExistsAsync(perfil))
            {
                Verificar(await roles.CreateAsync(new IdentityRole<Guid>(perfil)), $"el perfil {perfil}");
            }
        }

        var demo = DatosIniciales.OperadorDemoId;
        var este = DatosIniciales.OperadorEsteId;

        await CrearAsync(Usuario.CrearPersonal(demo, "Administración Demo", "admin@operador-demo.uy"), Perfil.Administrador);
        await CrearAsync(Usuario.CrearPersonal(demo, "Despacho Demo", "despacho@operador-demo.uy"), Perfil.Despachador);
        await CrearAsync(Usuario.CrearPersonal(este, "Administración Este", "admin@envios-del-este.uy"), Perfil.Administrador);
        await CrearAsync(Usuario.CrearPersonal(este, "Despacho Este", "despacho@envios-del-este.uy"), Perfil.Despachador);
        await CrearAsync(Usuario.CrearPersonal(demo, "Repartidor Demo", "repartidor@operador-demo.uy"), Perfil.Repartidor);
        await CrearAsync(Usuario.CrearPersonal(este, "Repartidor Este", "repartidor@envios-del-este.uy"), Perfil.Repartidor);
        await CrearAsync(Usuario.CrearPersonal(demo, "Depósito Demo", "deposito@operador-demo.uy"), Perfil.OperarioDeposito);
        await CrearAsync(Usuario.CrearPersonal(este, "Depósito Este", "deposito@envios-del-este.uy"), Perfil.OperarioDeposito);

        // Trabaja con los dos operadores: elige uno en la sesión (CU-03).
        await CrearAsync(Usuario.CrearDeComercio(DatosIniciales.ComercioDemoId, "Tienda Demo", "comercio@tiendademo.uy"),
            Perfil.UsuarioComercio);
    }

    private async Task CrearAsync(Usuario usuario, Perfil perfil)
    {
        if (await usuarios.FindByEmailAsync(usuario.Email!) is not null)
        {
            return;
        }

        Verificar(await usuarios.CreateAsync(usuario, ContrasenaDemo), usuario.Email!);
        Verificar(await usuarios.AddToRoleAsync(usuario, perfil.ToString()), usuario.Email!);
    }

    // Identity no lanza excepciones: devuelve un resultado. Si el seed falla, la API no arranca a medias.
    private static void Verificar(IdentityResult resultado, string que)
    {
        if (!resultado.Succeeded)
        {
            throw new InvalidOperationException(
                $"No se pudo cargar {que}: {string.Join(" ", resultado.Errors.Select(e => e.Description))}");
        }
    }
}

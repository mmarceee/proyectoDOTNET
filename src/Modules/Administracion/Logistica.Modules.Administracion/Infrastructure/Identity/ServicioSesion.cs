using Logistica.Modules.Administracion.Application.Features.IniciarSesion;
using Microsoft.AspNetCore.Identity;

namespace Logistica.Modules.Administracion.Infrastructure.Identity;

// Inicia la sesión con ASP.NET Core Identity: verifica la contraseña contra su hash y emite la cookie con los
// claims de FabricaDeClaims.
internal sealed class ServicioSesion(SignInManager<Usuario> sesiones, UserManager<Usuario> usuarios) : IServicioSesion
{
    public async Task<ResultadoInicioSesion> IniciarAsync(string email, string contrasena)
    {
        var usuario = await usuarios.FindByEmailAsync(email.Trim());

        // Un usuario desactivado no puede iniciar sesión (CU-02, A4). Se responde igual que a un correo que no
        // existe, para no revelar qué correos están registrados.
        if (usuario is null || !usuario.Activo)
        {
            return ResultadoInicioSesion.CredencialesInvalidas;
        }

        // lockoutOnFailure: después de 5 contraseñas incorrectas la cuenta se bloquea un rato (letra, sección 6.3).
        var resultado = await sesiones.PasswordSignInAsync(usuario, contrasena, isPersistent: false, lockoutOnFailure: true);

        if (resultado.Succeeded)
        {
            return ResultadoInicioSesion.Iniciada;
        }

        return resultado.IsLockedOut ? ResultadoInicioSesion.Bloqueada : ResultadoInicioSesion.CredencialesInvalidas;
    }

    public Task CerrarAsync()
    {
        return sesiones.SignOutAsync();
    }
}

using System.Security.Claims;
using Logistica.Modules.Administracion.Application.Features.IniciarSesion;
using Logistica.Modules.Administracion.Application.Features.SeleccionarOperador;
using Logistica.Modules.Administracion.Contracts;
using Microsoft.AspNetCore.Identity;
using Logistica.Modules.Administracion.Contracts;

namespace Logistica.Modules.Administracion.Infrastructure.Identity;

// Inicia la sesión con ASP.NET Core Identity: verifica la contraseña contra su hash y emite la cookie con los
// claims de FabricaDeClaims.
internal sealed class ServicioSesion(
    SignInManager<Usuario> sesiones, UserManager<Usuario> usuarios, IOperadoresDelComercio operadores) : IServicioSesion
{
    public async Task<ResultadoInicioSesion> IniciarAsync(string email, string contrasena, TipoAccesoSesion acceso = TipoAccesoSesion.Personal)
    {
        var usuario = await usuarios.FindByEmailAsync(email.Trim());

        // Un usuario desactivado no puede iniciar sesión (CU-02, A4). Se responde igual que a un correo que no
        // existe, para no revelar qué correos están registrados.
        if (usuario is null || !usuario.Activo)
        {
            return ResultadoInicioSesion.CredencialesInvalidas;
        }

        var perfiles = await usuarios.GetRolesAsync(usuario);
        var permitido = acceso switch
        {
            TipoAccesoSesion.Comercio => usuario.ComercioId is not null && perfiles.Contains(nameof(Perfil.UsuarioComercio)),
            TipoAccesoSesion.Repartidor => usuario.ComercioId is null && usuario.OperadorId is not null
                && perfiles.Contains(nameof(Perfil.Repartidor)),
            TipoAccesoSesion.Personal => usuario.ComercioId is null && perfiles.Any(p => p is nameof(Perfil.Administrador)
                or nameof(Perfil.Despachador) or nameof(Perfil.OperarioDeposito)),
            _ => false
        };
        if (!permitido) return ResultadoInicioSesion.CredencialesInvalidas;

        // lockoutOnFailure: después de 5 contraseñas incorrectas la cuenta se bloquea un rato (letra, sección 6.3).
        var resultado = await sesiones.PasswordSignInAsync(usuario, contrasena, isPersistent: false, lockoutOnFailure: true);

        if (resultado.Succeeded)
        {
            return usuario.ComercioId is null ? ResultadoInicioSesion.Iniciada : ResultadoInicioSesion.DebeElegirOperador;
        }

        return resultado.IsLockedOut ? ResultadoInicioSesion.Bloqueada : ResultadoInicioSesion.CredencialesInvalidas;
    }

    public async Task<bool> SeleccionarOperadorAsync(Guid usuarioId, Guid operadorId, CancellationToken ct)
    {
        // El comercio sale del usuario de la sesión, nunca del pedido.
        var usuario = await usuarios.FindByIdAsync(usuarioId.ToString());
        if (usuario is not { Activo: true, ComercioId: Guid comercioId })
        {
            return false;
        }

        // El servidor nunca acepta un operador enviado por el cliente sin verificar la relación (CU-03,
        // requerimiento especial 2).
        var disponibles = await operadores.ActivosAsync(comercioId, ct);
        if (disponibles.All(o => o.Id != operadorId))
        {
            return false;
        }

        // Elegir o cambiar de operador es volver a emitir la cookie: la elección no se guarda en la base
        // (ADR-0002, sección 2.6). FabricaDeClaims agrega el comercio; acá se suma el operador.
        await sesiones.SignInWithClaimsAsync(usuario, isPersistent: false,
            [new Claim(ClaimsDeSesion.OperadorId, operadorId.ToString())]);
        return true;
    }

    public Task CerrarAsync()
    {
        return sesiones.SignOutAsync();
    }
}

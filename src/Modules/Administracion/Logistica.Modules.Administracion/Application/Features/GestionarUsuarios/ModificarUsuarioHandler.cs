using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;

// CU-02: cambiar el perfil (A3), desactivar (A4) y reenviar la invitación (A6), con la regla A5. Cada acción busca el
// usuario dentro del alcance del administrador: si no es de su operador, devuelve false (como si no existiera).
// actorId, al desactivar, es el administrador de la sesión.
internal sealed class ModificarUsuarioHandler(
    ConsultarUsuariosHandler consultar, IServicioUsuarios usuarios, ICurrentTenant tenant)
{
    public async Task<bool> CambiarPerfilAsync(Guid usuarioId, Perfil nuevo, CancellationToken ct)
    {
        if (await consultar.ObtenerAsync(usuarioId, ct) is not UsuarioVista usuario)
        {
            return false;
        }

        // A3 es para el personal del operador; el usuario de comercio tiene siempre el mismo perfil.
        if (!usuario.EsPersonal || nuevo == Perfil.UsuarioComercio)
        {
            throw new DomainException("Sólo se cambia el perfil del personal, y a un perfil del operador.");
        }

        if (usuario.Perfil == Perfil.Administrador && nuevo != Perfil.Administrador)
        {
            await ExigirOtroAdministradorAsync(usuario);
        }

        await usuarios.CambiarPerfilAsync(usuarioId, nuevo, ct);
        return true;
    }

    public async Task<bool> DesactivarAsync(Guid usuarioId, Guid actorId, CancellationToken ct)
    {
        if (await consultar.ObtenerAsync(usuarioId, ct) is not UsuarioVista usuario)
        {
            return false;
        }

        // A5: nadie se desactiva a sí mismo.
        if (usuarioId == actorId)
        {
            throw new DomainException("No podés desactivar tu propio usuario.");
        }

        if (usuario.Perfil == Perfil.Administrador && usuario.Estado == EstadoUsuario.Activo)
        {
            await ExigirOtroAdministradorAsync(usuario);
        }

        await usuarios.DesactivarAsync(usuarioId, ct);
        return true;
    }

    public async Task<Invitacion?> ReenviarInvitacionAsync(Guid usuarioId, CancellationToken ct)
    {
        if (await consultar.ObtenerAsync(usuarioId, ct) is not UsuarioVista usuario)
        {
            return null;
        }

        // A6: sólo tiene sentido mientras no definió su contraseña.
        if (usuario.Estado != EstadoUsuario.PendienteDeActivacion)
        {
            throw new DomainException("El usuario ya activó su cuenta.");
        }

        return await usuarios.ReenviarInvitacionAsync(usuarioId, ct);
    }

    // A5: el operador no puede quedarse sin administradores activos.
    private async Task ExigirOtroAdministradorAsync(UsuarioVista usuario)
    {
        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        if (await usuarios.AdministradoresActivosAsync(operadorId) <= 1)
        {
            throw new DomainException("El operador tiene que tener al menos un administrador activo.");
        }
    }
}

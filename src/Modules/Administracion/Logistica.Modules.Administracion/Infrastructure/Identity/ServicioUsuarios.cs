using Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Events;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Logistica.Modules.Administracion.Infrastructure.Identity.ErroresDeIdentity;

namespace Logistica.Modules.Administracion.Infrastructure.Identity;

// CU-02 con ASP.NET Core Identity. Es la ÚNICA clase que consulta la tabla de usuarios para el personal: como Usuario no
// tiene filtro "Tenant", cada consulta filtra explícitamente por el operador y los comercios que recibe (ADR-0002,
// sección 2.4, "cómo se compensa la falta de filtro").
internal sealed class ServicioUsuarios(
    UserManager<Usuario> usuarios,
    IdentidadDbContext identidad,
    AdministracionDbContext administracion,
    IUnidadDeTrabajo unidadDeTrabajo,
    VinculoRepartidor vinculos,
    TimeProvider reloj) : IServicioUsuarios
{
    public async Task<IReadOnlyList<UsuarioResumen>> ListarAsync(
        Guid operadorId, IReadOnlyCollection<Guid> comercios, CancellationToken ct)
    {
        var lista = await DelAlcance(operadorId, comercios).OrderBy(u => u.Nombre).ToListAsync(ct);
        return await ResumirAsync(lista, ct);
    }

    public async Task<UsuarioResumen?> ObtenerAsync(
        Guid usuarioId, Guid operadorId, IReadOnlyCollection<Guid> comercios, CancellationToken ct)
    {
        var usuario = await DelAlcance(operadorId, comercios).SingleOrDefaultAsync(u => u.Id == usuarioId, ct);
        return usuario is null ? null : (await ResumirAsync([usuario], ct))[0];
    }

    public async Task<bool> CorreoRegistradoAsync(string email)
    {
        return await usuarios.FindByEmailAsync(email.Trim()) is not null;
    }

    public async Task<int> AdministradoresActivosAsync(Guid operadorId)
    {
        var administradores = await usuarios.GetUsersInRoleAsync(nameof(Perfil.Administrador));
        return administradores.Count(u => u.OperadorId == operadorId && u.Activo && !u.PendienteDeActivacion);
    }

    public async Task<Invitacion> CrearAsync(NuevoUsuario nuevo, CancellationToken ct)
    {
        var usuario = nuevo.ComercioId is Guid comercioId
            ? Usuario.CrearDeComercio(comercioId, nuevo.Nombre, nuevo.Email)
            : Usuario.CrearPersonal(nuevo.OperadorId!.Value, nuevo.Nombre, nuevo.Email);

        Invitacion? invitacion = null;

        // Todo o nada (ADR-0003): el usuario, su perfil, el vínculo con el repartidor y el evento de la invitación.
        await unidadDeTrabajo.EjecutarAsync(async () =>
        {
            // Sin contraseña: queda pendiente de activación (paso 6).
            Verificar(await usuarios.CreateAsync(usuario));
            Verificar(await usuarios.AddToRoleAsync(usuario, nuevo.Perfil.ToString()));

            if (nuevo.RepartidorId is Guid repartidorId)
            {
                await vinculos.VincularAsync(nuevo.OperadorId!.Value, repartidorId, usuario.Id, ct);
            }

            invitacion = await RegistrarInvitacionAsync(usuario, nuevo.Perfil, reenvio: false, ct);
        }, ct);

        return invitacion!;
    }

    public async Task<Invitacion> ReenviarInvitacionAsync(Guid usuarioId, CancellationToken ct)
    {
        var usuario = await BuscarAsync(usuarioId);
        var perfil = await PerfilDeAsync(usuario);

        Invitacion? invitacion = null;
        await unidadDeTrabajo.EjecutarAsync(async () =>
            invitacion = await RegistrarInvitacionAsync(usuario, perfil, reenvio: true, ct), ct);

        return invitacion!;
    }

    public async Task CambiarPerfilAsync(Guid usuarioId, Perfil perfil, CancellationToken ct)
    {
        var usuario = await BuscarAsync(usuarioId);

        await unidadDeTrabajo.EjecutarAsync(async () =>
        {
            Verificar(await usuarios.RemoveFromRolesAsync(usuario, await usuarios.GetRolesAsync(usuario)));
            Verificar(await usuarios.AddToRoleAsync(usuario, perfil.ToString()));

            // A3: la cookie se rearma con el perfil nuevo en la próxima validación del security stamp.
            Verificar(await usuarios.UpdateSecurityStampAsync(usuario));
        }, ct);
    }

    public async Task DesactivarAsync(Guid usuarioId, CancellationToken ct)
    {
        var usuario = await BuscarAsync(usuarioId);
        usuario.Desactivar();

        // A4: cambiar el security stamp invalida las cookies abiertas en su próxima validación; guarda también Activo.
        Verificar(await usuarios.UpdateSecurityStampAsync(usuario));
    }

    public async Task<ResultadoActivacion> ActivarAsync(Guid usuarioId, string token, string contrasena)
    {
        var usuario = await usuarios.FindByIdAsync(usuarioId.ToString());

        // Un enlace ya usado deja al usuario con contraseña: igual que uno vencido o inventado.
        if (usuario is null || !usuario.Activo || !usuario.PendienteDeActivacion)
        {
            return new ResultadoActivacion(false, true, [], null);
        }

        // Verifica el token (48 horas, un solo uso) y la política de contraseñas; si todo va bien, cambia el security stamp.
        var resultado = await usuarios.ResetPasswordAsync(usuario, token, contrasena);
        if (!resultado.Succeeded)
        {
            return resultado.Errors.Any(e => e.Code == "InvalidToken")
                ? new ResultadoActivacion(false, true, [], null)
                : new ResultadoActivacion(false, false, resultado.Errors.Select(Traducir).Distinct().ToList(), null);
        }

        // Quien tiene el enlace del correo tiene el correo: queda confirmado.
        usuario.EmailConfirmed = true;
        Verificar(await usuarios.UpdateAsync(usuario));

        return new ResultadoActivacion(true, false, [], await PerfilDeAsync(usuario));
    }

    // El alcance del administrador: su personal y los usuarios de sus comercios.
    private IQueryable<Usuario> DelAlcance(Guid operadorId, IReadOnlyCollection<Guid> comercios)
    {
        var ids = comercios.ToList();
        return identidad.Users.AsNoTracking()
            .Where(u => u.OperadorId == operadorId || (u.ComercioId != null && ids.Contains(u.ComercioId.Value)));
    }

    private async Task<IReadOnlyList<UsuarioResumen>> ResumirAsync(IReadOnlyList<Usuario> lista, CancellationToken ct)
    {
        var ids = lista.Select(u => u.Id).ToList();
        var perfiles = await (
                from ur in identidad.UserRoles
                join r in identidad.Roles on ur.RoleId equals r.Id
                where ids.Contains(ur.UserId)
                select new { ur.UserId, r.Name })
            .ToDictionaryAsync(x => x.UserId, x => Enum.Parse<Perfil>(x.Name!), ct);

        return lista.Select(u => new UsuarioResumen(
            u.Id, u.Nombre, u.Email!, perfiles[u.Id], u.ComercioId, u.Activo, u.PendienteDeActivacion)).ToList();
    }

    private async Task<Usuario> BuscarAsync(Guid usuarioId)
    {
        return await usuarios.FindByIdAsync(usuarioId.ToString())
            ?? throw new InvalidOperationException("El usuario no existe.");
    }

    private async Task<Perfil> PerfilDeAsync(Usuario usuario)
    {
        return Enum.Parse<Perfil>((await usuarios.GetRolesAsync(usuario)).Single());
    }

    // Paso 7 y A6: un token nuevo (48 horas, un solo uso) y el evento para que el Worker mande el correo. Se guarda en el
    // outbox de Administración, dentro de la transacción de quien llama.
    private async Task<Invitacion> RegistrarInvitacionAsync(Usuario usuario, Perfil perfil, bool reenvio, CancellationToken ct)
    {
        var token = await usuarios.GeneratePasswordResetTokenAsync(usuario);
        var enlace = $"/cuenta/activar?usuario={usuario.Id}&token={Uri.EscapeDataString(token)}";

        administracion.AgregarAlOutbox(UsuarioInvitado.Tipo,
            new UsuarioInvitado(usuario.Id, usuario.Email!, usuario.Nombre, perfil.ToString(), enlace, reenvio),
            reloj.GetUtcNow());
        await administracion.SaveChangesAsync(ct);

        return new Invitacion(usuario.Id, enlace);
    }
}

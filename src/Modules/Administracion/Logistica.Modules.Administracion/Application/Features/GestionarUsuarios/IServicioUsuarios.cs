using Logistica.Modules.Administracion.Contracts;

namespace Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;

// Los usuarios de Identity, para CU-02. Lo implementa Infrastructure con ASP.NET Core Identity: Application y Presentation
// no conocen Identity ni el Usuario (ADR-0001, sección 2.4).
// Usuario no tiene filtro "Tenant" (ADR-0002, sección 2.4): ésta es la ÚNICA puerta a la tabla de usuarios, y toda
// consulta recibe el operador de la sesión y los comercios que trabajan con él.
internal interface IServicioUsuarios
{
    // Paso 2: el personal del operador y los usuarios de sus comercios.
    Task<IReadOnlyList<UsuarioResumen>> ListarAsync(Guid operadorId, IReadOnlyCollection<Guid> comercios, CancellationToken ct);

    // Un usuario, sólo si es del operador o de uno de sus comercios; si no, null (como si no existiera).
    Task<UsuarioResumen?> ObtenerAsync(Guid usuarioId, Guid operadorId, IReadOnlyCollection<Guid> comercios, CancellationToken ct);

    // CU-43: cuentas del operador activadas, activas y con perfil Repartidor. Consulta por lote;
    // no expone usuarios de otro operador ni datos de Identity a Planificación.
    Task<IReadOnlySet<Guid>> UsuariosRepartidoresHabilitadosAsync(Guid operadorId, IReadOnlyCollection<Guid> usuarioIds, CancellationToken ct);

    Task<bool> CorreoRegistradoAsync(string email);

    // A5: cuántos administradores activos le quedan al operador.
    Task<int> AdministradoresActivosAsync(Guid operadorId);

    // Pasos 6 y 7: crea el usuario pendiente de activación y registra la invitación en el outbox, en una transacción.
    Task<Invitacion> CrearAsync(NuevoUsuario nuevo, CancellationToken ct);

    // A6: un enlace nuevo, si el usuario todavía no activó su cuenta.
    Task<Invitacion> ReenviarInvitacionAsync(Guid usuarioId, CancellationToken ct);

    // A3: rige desde la próxima validación de la cookie (cambia el security stamp).
    Task CambiarPerfilAsync(Guid usuarioId, Perfil perfil, CancellationToken ct);

    // A4: además cierra sus sesiones abiertas (cambia el security stamp).
    Task DesactivarAsync(Guid usuarioId, CancellationToken ct);

    // El usuario define su contraseña con el enlace de la invitación.
    Task<ResultadoActivacion> ActivarAsync(Guid usuarioId, string token, string contrasena);
}

internal sealed record UsuarioResumen(
    Guid Id, string Nombre, string Email, Perfil Perfil, Guid? ComercioId, bool Activo, bool PendienteDeActivacion);

// OperadorId para el personal, ComercioId para el usuario de comercio: nunca los dos.
internal sealed record NuevoUsuario(
    Guid? OperadorId, Guid? ComercioId, string Nombre, string Email, Perfil Perfil, Guid? RepartidorId);

// El enlace es relativo: /cuenta/activar?usuario=...&token=...
internal sealed record Invitacion(Guid UsuarioId, string Enlace);

internal sealed record ResultadoActivacion(bool Activada, bool EnlaceInvalido, IReadOnlyList<string> Errores, Perfil? Perfil);

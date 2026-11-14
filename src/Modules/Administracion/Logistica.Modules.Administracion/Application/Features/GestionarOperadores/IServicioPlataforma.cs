using Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;
using Logistica.Modules.Administracion.Domain.Operadores;

namespace Logistica.Modules.Administracion.Application.Features.GestionarOperadores;

// Lo que el administrador general hace dentro de un operador. Su sesión no tiene operador, y el outbox exige uno
// (ADR-0002, sección 2.5): la implementación trabaja en un ámbito propio con el operador fijado (InquilinoTrabajo), como
// el cierre diario. El operador sale del caso de uso, nunca del formulario de un usuario de un operador.
internal interface IServicioPlataforma
{
    // Crea el operador y su primer administrador, con la invitación en el outbox, en una sola transacción.
    Task<Invitacion> CrearOperadorAsync(Operador operador, string nombreAdministrador, string emailAdministrador,
        CancellationToken ct);

    // Otro administrador para un operador que ya existe.
    Task<Invitacion> InvitarAdministradorAsync(Guid operadorId, string nombre, string email, CancellationToken ct);
}

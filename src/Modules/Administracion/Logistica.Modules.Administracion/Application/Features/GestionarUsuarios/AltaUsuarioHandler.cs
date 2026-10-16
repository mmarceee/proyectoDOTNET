using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;

// Lo que ingresa el administrador (pasos 3 y 4). El operador no viene acá: es el de la sesión.
internal sealed record AltaUsuarioCommand(string Nombre, string Email, Perfil Perfil, Guid? ComercioId, Guid? RepartidorId);

// CU-02, alta (pasos 3 a 7). Coordina y aplica las reglas del caso de uso; las de cada dato (nombre y correo
// presentes) las valida Usuario.
internal sealed class AltaUsuarioHandler(IServicioUsuarios usuarios, IDatosParaUsuarios datos, ICurrentTenant tenant)
{
    public async Task<Invitacion> HandleAsync(AltaUsuarioCommand command, CancellationToken ct)
    {
        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("La sesión no tiene un operador.");

        // A2: datos inválidos.
        if (string.IsNullOrWhiteSpace(command.Email) || !command.Email.Contains('@'))
        {
            throw new DomainException("El correo no es válido.");
        }

        // A1: correo ya registrado (un correo, un usuario en todo el sistema; ADR-0002, sección 5).
        if (await usuarios.CorreoRegistradoAsync(command.Email))
        {
            throw new DomainException("Ya existe un usuario con ese correo.");
        }

        if (command.Perfil == Perfil.UsuarioComercio)
        {
            // Precondición 2: el comercio tiene una relación activa con este operador.
            var comercios = await datos.ComerciosAsync(ct);
            if (command.ComercioId is not Guid comercioId || !comercios.Any(c => c.Id == comercioId && c.Activa))
            {
                throw new DomainException("Elegí un comercio con una relación activa con el operador.");
            }

            return await usuarios.CrearAsync(
                new NuevoUsuario(null, comercioId, command.Nombre, command.Email, command.Perfil, null), ct);
        }

        // Paso 4: sólo un repartidor se vincula con un Repartidor, y sólo con uno de este operador sin usuario.
        if (command.RepartidorId is Guid repartidorId)
        {
            var disponibles = await datos.RepartidoresSinUsuarioAsync(ct);
            if (command.Perfil != Perfil.Repartidor || disponibles.All(r => r.Id != repartidorId))
            {
                throw new DomainException("Sólo un usuario repartidor se vincula, y con un repartidor sin usuario.");
            }
        }

        // El operador sale de la sesión, nunca del formulario: acá no actúa el interceptor (ADR-0002, sección 2.4).
        return await usuarios.CrearAsync(
            new NuevoUsuario(operadorId, null, command.Nombre, command.Email, command.Perfil, command.RepartidorId), ct);
    }
}

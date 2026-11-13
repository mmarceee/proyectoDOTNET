using System.Text.RegularExpressions;
using Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Operadores;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.GestionarOperadores;

internal sealed record CrearOperadorCommand(
    string Nombre, string Slug, string ZonaHoraria, string NombreAdministrador, string EmailAdministrador);

internal sealed record OperadorResumen(Guid Id, string Nombre, string Slug, string ZonaHoraria, bool Activo);

internal sealed record AdministradorDeOperador(Guid Id, string Nombre, string Email, EstadoUsuario Estado);

internal sealed record DetalleOperador(OperadorResumen Operador, IReadOnlyList<AdministradorDeOperador> Administradores);

internal sealed record OperadorCreado(Guid OperadorId, Invitacion Invitacion);

// Administrador general: alta, suspensión y activación de operadores, y sus administradores. Operador es global y
// Usuario no tiene filtro (ADR-0002, sección 2.4), así que el operador llega por parámetro: éste es el único perfil que
// trabaja sobre cualquier operador, y la página lo exige con la política AdministradorGeneral.
internal sealed partial class GestionarOperadoresHandler(
    IOperadorRepository operadores, IServicioUsuarios usuarios, IServicioPlataforma plataforma)
{
    public async Task<IReadOnlyList<OperadorResumen>> ListarAsync(CancellationToken ct) =>
        (await operadores.ListarAsync(ct)).Select(Resumir).ToList();

    // null si el operador no existe: la página responde 404.
    public async Task<DetalleOperador?> ObtenerAsync(Guid operadorId, CancellationToken ct)
    {
        if (await operadores.ObtenerAsync(operadorId, ct) is not { } operador)
        {
            return null;
        }

        var administradores = (await usuarios.ListarAsync(operadorId, [], ct))
            .Where(u => u.Perfil == Perfil.Administrador)
            .Select(u => new AdministradorDeOperador(u.Id, u.Nombre, u.Email, EstadoDe(u)))
            .ToList();

        return new DetalleOperador(Resumir(operador), administradores);
    }

    public async Task<OperadorCreado> CrearAsync(CrearOperadorCommand command, CancellationToken ct)
    {
        await ValidarCorreoAsync(command.EmailAdministrador);

        var slug = (command.Slug ?? "").Trim().ToLowerInvariant();
        if (!FormatoSlug().IsMatch(slug))
        {
            throw new DomainException("El slug sólo admite minúsculas, números y guiones (por ejemplo, envios-del-sur).");
        }

        if (await operadores.ExisteSlugAsync(slug, ct))
        {
            throw new DomainException("Ya existe un operador con ese slug.");
        }

        var zonaHoraria = (command.ZonaHoraria ?? "").Trim();
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zonaHoraria, out _))
        {
            throw new DomainException("La zona horaria no existe. Usá un nombre como America/Montevideo.");
        }

        // Nombre, slug y zona presentes los valida Operador; nombre del administrador, Usuario.
        var operador = Operador.Crear(command.Nombre?.Trim() ?? "", slug, zonaHoraria);
        var invitacion = await plataforma.CrearOperadorAsync(operador, command.NombreAdministrador,
            command.EmailAdministrador.Trim(), ct);

        return new OperadorCreado(operador.Id, invitacion);
    }

    public Task<bool> SuspenderAsync(Guid operadorId, CancellationToken ct) =>
        CambiarAsync(operadorId, o => o.Suspender(), ct);

    public Task<bool> ActivarAsync(Guid operadorId, CancellationToken ct) =>
        CambiarAsync(operadorId, o => o.Activar(), ct);

    // null si el operador no existe.
    public async Task<Invitacion?> AgregarAdministradorAsync(Guid operadorId, string nombre, string email, CancellationToken ct)
    {
        if (await operadores.ObtenerAsync(operadorId, ct) is null)
        {
            return null;
        }

        await ValidarCorreoAsync(email);
        return await plataforma.InvitarAdministradorAsync(operadorId, nombre, email.Trim(), ct);
    }

    // false si el usuario no es un administrador de ese operador. Al administrador general no le aplica CU-02, A5 (no
    // dejar al operador sin administradores): puede invitar otro en cualquier momento.
    public async Task<bool> DesactivarAdministradorAsync(Guid operadorId, Guid usuarioId, CancellationToken ct)
    {
        var usuario = await usuarios.ObtenerAsync(usuarioId, operadorId, [], ct);
        if (usuario is not { Perfil: Perfil.Administrador })
        {
            return false;
        }

        if (!usuario.Activo)
        {
            throw new DomainException("El administrador ya está desactivado.");
        }

        await usuarios.DesactivarAsync(usuarioId, ct);
        return true;
    }

    private async Task<bool> CambiarAsync(Guid operadorId, Action<Operador> cambio, CancellationToken ct)
    {
        if (await operadores.ObtenerAsync(operadorId, ct) is not { } operador)
        {
            return false;
        }

        cambio(operador);
        await operadores.GuardarCambiosAsync(ct);
        return true;
    }

    // Igual que CU-02, A1 y A2: un correo, un usuario en todo el sistema.
    private async Task ValidarCorreoAsync(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            throw new DomainException("El correo no es válido.");
        }

        if (await usuarios.CorreoRegistradoAsync(email))
        {
            throw new DomainException("Ya existe un usuario con ese correo.");
        }
    }

    private static OperadorResumen Resumir(Operador o) => new(o.Id, o.Nombre, o.Slug, o.ZonaHoraria, o.Activo);

    private static EstadoUsuario EstadoDe(UsuarioResumen u) =>
        !u.Activo ? EstadoUsuario.Desactivado
        : u.PendienteDeActivacion ? EstadoUsuario.PendienteDeActivacion
        : EstadoUsuario.Activo;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex FormatoSlug();
}

using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;

internal enum EstadoUsuario
{
    Activo,
    PendienteDeActivacion,
    Desactivado,
}

internal sealed record UsuarioVista(
    Guid Id, string Nombre, string Email, Perfil Perfil, string? Comercio, EstadoUsuario Estado, bool EsPersonal);

// CU-02, paso 2: los usuarios del operador de la sesión y los de los comercios que trabajan con él.
internal sealed class ConsultarUsuariosHandler(IServicioUsuarios usuarios, IDatosParaUsuarios datos, ICurrentTenant tenant)
{
    public async Task<IReadOnlyList<UsuarioVista>> ListarAsync(CancellationToken ct)
    {
        var (operadorId, comercios) = await AlcanceAsync(ct);
        var lista = await usuarios.ListarAsync(operadorId, comercios.Keys, ct);
        return lista.Select(u => Vista(u, comercios)).ToList();
    }

    // null: no existe o no es de este operador (el administrador no puede ver usuarios de otro, ADR-0002, sección 2.9).
    public async Task<UsuarioVista?> ObtenerAsync(Guid usuarioId, CancellationToken ct)
    {
        var (operadorId, comercios) = await AlcanceAsync(ct);
        var usuario = await usuarios.ObtenerAsync(usuarioId, operadorId, comercios.Keys, ct);
        return usuario is null ? null : Vista(usuario, comercios);
    }

    // El alcance del administrador: su operador (de la sesión) y los comercios que trabajan con él.
    private async Task<(Guid OperadorId, Dictionary<Guid, string> Comercios)> AlcanceAsync(CancellationToken ct)
    {
        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        var comercios = (await datos.ComerciosAsync(ct)).ToDictionary(c => c.Id, c => c.RazonSocial);
        return (operadorId, comercios);
    }

    private static UsuarioVista Vista(UsuarioResumen u, IReadOnlyDictionary<Guid, string> comercios)
    {
        var estado = u.PendienteDeActivacion ? EstadoUsuario.PendienteDeActivacion
            : u.Activo ? EstadoUsuario.Activo
            : EstadoUsuario.Desactivado;
        var comercio = u.ComercioId is Guid id ? comercios.GetValueOrDefault(id) : null;
        return new UsuarioVista(u.Id, u.Nombre, u.Email, u.Perfil, comercio, estado, u.ComercioId is null);
    }
}

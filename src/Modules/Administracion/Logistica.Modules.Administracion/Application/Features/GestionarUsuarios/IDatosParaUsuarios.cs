namespace Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;

// Lo que CU-02 necesita de las tablas del operador. Pasa por el filtro "Tenant": sólo ve lo del operador de la sesión.
internal interface IDatosParaUsuarios
{
    // Los comercios que trabajan con el operador; Activa indica si la relación está activa (precondición 2 del alta).
    Task<IReadOnlyList<ComercioDelOperador>> ComerciosAsync(CancellationToken ct);

    // Paso 4: los repartidores activos que todavía no tienen usuario.
    Task<IReadOnlyList<RepartidorSinUsuario>> RepartidoresSinUsuarioAsync(CancellationToken ct);
}

internal sealed record ComercioDelOperador(Guid Id, string RazonSocial, bool Activa);

internal sealed record RepartidorSinUsuario(Guid Id, string Nombre, string Documento);

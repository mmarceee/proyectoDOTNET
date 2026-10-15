namespace Logistica.Modules.Administracion.Application.Features.IniciarSesion;

// Inicio y cierre de sesión. Lo implementa Infrastructure con ASP.NET Core Identity: Application y Presentation
// no conocen Identity ni el Usuario (ADR-0001, sección 2.4).
internal interface IServicioSesion
{
    Task<ResultadoInicioSesion> IniciarAsync(string email, string contrasena, TipoAccesoSesion acceso = TipoAccesoSesion.Personal);

    // CU-03: vuelve a emitir la cookie con el operador elegido, si el comercio del usuario tiene una relación activa
    // con él. El usuario es el de la sesión: Presentation lo toma de la cookie. false: no es un usuario de comercio
    // activo, o ese operador no está entre los suyos.
    Task<bool> SeleccionarOperadorAsync(Guid usuarioId, Guid operadorId, CancellationToken ct);

    Task CerrarAsync();
}

internal enum ResultadoInicioSesion
{
    Iniciada,

    // Usuario de comercio: entró, pero no ve datos hasta elegir el operador de la sesión (CU-03).
    DebeElegirOperador,

    CredencialesInvalidas,
    Bloqueada,
}

internal enum TipoAccesoSesion { Personal, Comercio, Repartidor }

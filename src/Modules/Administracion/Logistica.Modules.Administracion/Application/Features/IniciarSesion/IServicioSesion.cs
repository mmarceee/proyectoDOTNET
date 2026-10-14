namespace Logistica.Modules.Administracion.Application.Features.IniciarSesion;

// Inicio y cierre de sesión. Lo implementa Infrastructure con ASP.NET Core Identity: Application y Presentation
// no conocen Identity ni el Usuario (ADR-0001, sección 2.4).
internal interface IServicioSesion
{
    Task<ResultadoInicioSesion> IniciarAsync(string email, string contrasena);

    Task CerrarAsync();
}

internal enum ResultadoInicioSesion
{
    Iniciada,
    CredencialesInvalidas,
    Bloqueada,
}

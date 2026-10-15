using Logistica.Modules.Administracion.Application.Features.GestionarIdentidadVisual;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarIdentidadVisual;

// La marca del operador de la sesión para los layouts (CU-07; RF 5): los colores como hoja de estilos y el logo.
// Los layouts sólo enlazan estas direcciones, sin conocer los módulos (ADR-0001, addendum 4). Sin operador en la
// sesión responden la marca por defecto (estilos vacíos y sin logo).
internal static class MarcaDelOperadorEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/cuenta/estilo.css", EstiloAsync);
        endpoints.MapGet("/cuenta/logo", LogoAsync);
    }

    private static async Task<IResult> EstiloAsync(ConsultarIdentidadVisualHandler consultar, HttpContext http,
        CancellationToken ct)
    {
        // Cambia con la sesión: el navegador no la guarda.
        http.Response.Headers.CacheControl = "no-store";

        if (await consultar.HandleAsync(ct) is not IdentidadVisualDto identidad)
        {
            return Results.Text("/* Sin operador en la sesión: estilos por defecto. */", "text/css");
        }

        // Los colores ya pasaron por IdentidadVisual (#RRGGBB): no pueden inyectar CSS.
        var css = $$"""
            :root { --operador-primario: {{identidad.ColorPrimario}}; --operador-secundario: {{identidad.ColorSecundario}}; }

            /* Backoffice y páginas de la cuenta */
            header .navbar, header.barra-operador {
                background-color: var(--operador-primario) !important;
                border-bottom: 4px solid var(--operador-secundario) !important;
            }
            header .navbar .navbar-brand, header .navbar .nav-link, header .navbar .navbar-text,
            header.barra-operador .navbar-brand { color: #fff !important; }

            /* Portal del comercio (Blazor) */
            .sidebar { background-image: none !important; background-color: var(--operador-primario) !important; }
            .top-row { border-bottom: 4px solid var(--operador-secundario) !important; }

            .btn-primary {
                --bs-btn-bg: var(--operador-primario); --bs-btn-border-color: var(--operador-primario);
                --bs-btn-hover-bg: var(--operador-primario); --bs-btn-hover-border-color: var(--operador-primario);
            }
            """;

        return Results.Text(css, "text/css");
    }

    private static async Task<IResult> LogoAsync(ConsultarIdentidadVisualHandler consultar, HttpContext http,
        CancellationToken ct)
    {
        if (await consultar.LogoAsync(ct) is not LogoDto logo)
        {
            return Results.NotFound();
        }

        // Un SVG puede traer scripts: se sirve sin permiso para ejecutar nada (letra, sección 6.3).
        http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.CacheControl = "no-store";

        return Results.File(logo.Contenido, logo.TipoContenido);
    }
}

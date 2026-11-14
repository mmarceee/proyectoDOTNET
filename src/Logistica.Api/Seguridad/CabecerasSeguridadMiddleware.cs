namespace Logistica.Api.Seguridad;

// Cabeceras seguras en todas las respuestas (letra, sección 6.3): la API, el Backoffice, las aplicaciones WebAssembly y
// los archivos estáticos salen del mismo origen, así que una sola política las cubre.
// - X-Content-Type-Options: el navegador no adivina el tipo de un archivo (una foto subida no se ejecuta como script).
// - X-Frame-Options y frame-ancestors: ninguna página se puede incrustar en otro sitio (clickjacking).
// - Referrer-Policy: a otro sitio sólo se le manda el origen, nunca la ruta: el enlace del seguimiento público lleva su
//   token en la URL y no debe filtrarse (por ejemplo, al pedir los mapas del tablero).
// - Permissions-Policy: cámara y ubicación sólo para este origen (la PWA las usa); micrófono y pagos, para nadie.
// - Content-Security-Policy: object-src, base-uri, form-action y frame-ancestors. No restringe scripts ni estilos: el
//   tablero carga Leaflet y SignalR de cdnjs y tiene scripts en línea, y Blazor WebAssembly necesita compilar su código.
//   Restringirlos exige nonces en cada página; queda como mejora.
// - Strict-Transport-Security fuera de Development: en Azure el ingreso es HTTPS (los navegadores ignoran HSTS en HTTP).
// Un endpoint puede pisar una cabecera con su propio valor (la evidencia fija la suya).
internal sealed class CabecerasSeguridadMiddleware(RequestDelegate next, IHostEnvironment entorno)
{
    public const string PoliticaContenido =
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext http)
    {
        var cabeceras = http.Response.Headers;
        cabeceras["X-Content-Type-Options"] = "nosniff";
        cabeceras["X-Frame-Options"] = "DENY";
        cabeceras["Referrer-Policy"] = "strict-origin-when-cross-origin";
        cabeceras["Permissions-Policy"] = "camera=(self), geolocation=(self), microphone=(), payment=()";
        cabeceras.ContentSecurityPolicy = PoliticaContenido;

        if (!entorno.IsDevelopment())
        {
            cabeceras.StrictTransportSecurity = "max-age=31536000";
        }

        return next(http);
    }
}

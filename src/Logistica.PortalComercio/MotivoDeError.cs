using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Logistica.PortalComercio;

// El motivo que se le muestra al comercio cuando la API rechaza un pedido. Nunca queda vacío: si la respuesta trae un
// ProblemDetails con "detail" (reglas de negocio, 409, 503) se usa ese texto; si trae errores por campo, se listan; y si
// no trae nada (401, 403, 404, 500) se explica según el código. Ante un error interno se agrega la referencia (traceId)
// para buscarlo en los registros del servidor, sin mostrar detalles internos.
public static class MotivoDeError
{
    public static async Task<string> LeerAsync(HttpResponseMessage respuesta, CancellationToken ct = default)
    {
        var problema = await LeerProblemaAsync(respuesta, ct);

        if (!string.IsNullOrWhiteSpace(problema?.Detail))
        {
            return problema.Detail;
        }

        if (problema?.Errors is { Count: > 0 } errores)
        {
            return "Revisá los datos: " + string.Join(" ", errores.SelectMany(e => e.Value).Distinct());
        }

        var codigo = (int)respuesta.StatusCode;
        var motivo = respuesta.StatusCode switch
        {
            HttpStatusCode.BadRequest => "Hay datos con un formato inválido. Revisá los campos e intentá de nuevo.",
            HttpStatusCode.Unauthorized => "Tu sesión venció. Volvé a ingresar al Portal e intentá de nuevo.",
            HttpStatusCode.Forbidden =>
                "Tu sesión ya no tiene un operador elegido, o tu comercio dejó de trabajar con él. Volvé a elegir el operador.",
            HttpStatusCode.NotFound => "No se encontró lo que pediste. Puede que ya no exista o que sea de otro operador.",
            HttpStatusCode.Conflict => "Los datos cambiaron mientras se procesaba el pedido. Intentá de nuevo.",
            HttpStatusCode.ServiceUnavailable => "El servicio no está disponible en este momento. Intentá de nuevo en unos segundos.",
            _ when codigo >= 500 => "Ocurrió un error inesperado en el servidor. Intentá de nuevo; si se repite, avisá al operador.",
            _ => $"El servidor rechazó el pedido (código {codigo}).",
        };

        return codigo >= 500 && problema?.TraceId is { Length: > 0 } referencia
            ? $"{motivo} Referencia: {referencia}."
            : motivo;
    }

    // Un error de red o un tiempo de espera agotado: el pedido no llegó o no volvió la respuesta.
    public static string DeConexion(Exception error) => error is TaskCanceledException
        ? "El servidor tardó demasiado en responder. Revisá en el listado si el envío se creó antes de intentar de nuevo."
        : "No se pudo conectar con el servidor. Revisá tu conexión e intentá de nuevo.";

    private static async Task<Problema?> LeerProblemaAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            return respuesta.Content.Headers.ContentLength == 0
                ? null
                : await respuesta.Content.ReadFromJsonAsync<Problema>(ct);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            // Sin cuerpo JSON (por ejemplo, un 401 o un 404 vacíos): se explica por el código.
            return null;
        }
    }

    private sealed record Problema(string? Title, string? Detail, string? TraceId,
        Dictionary<string, string[]>? Errors);
}

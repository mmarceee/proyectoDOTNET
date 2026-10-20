using System.Net.Http.Headers;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Infrastructure.Security;

namespace Logistica.Modules.Seguimiento.Infrastructure.Http;

internal sealed class EmisorAvisosHttp(HttpClient http, TimeProvider reloj) : IEmisorAvisos
{
    public async Task<ResultadoEnvioAviso> EnviarAsync(string urlDestino, string secretoFirma, MensajeAviso mensaje, CancellationToken ct)
    {
        if (!Uri.TryCreate(urlDestino, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
            url.UserInfo.Length != 0 || url.Fragment.Length != 0)
            return new(false, null, "La URL receptora debe usar HTTPS, sin credenciales ni fragmentos.");
        var cuerpo = FirmadorAvisos.Serializar(mensaje);
        var fecha = FirmadorAvisos.Fecha(reloj.GetUtcNow());
        using var peticion = new HttpRequestMessage(HttpMethod.Post, url);
        peticion.Content = new ByteArrayContent(cuerpo);
        peticion.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        peticion.Headers.Add("X-Aviso-Fecha", fecha);
        peticion.Headers.Add("X-Aviso-Firma", FirmadorAvisos.Firmar(cuerpo, fecha, secretoFirma));
        try
        {
            using var respuesta = await http.SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, ct);
            return new(respuesta.IsSuccessStatusCode, (int)respuesta.StatusCode,
                respuesta.IsSuccessStatusCode ? null : "El sistema receptor respondió con un código distinto de 2xx.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return new(false, null, "El sistema receptor no respondió dentro de los 10 segundos."); }
        catch (HttpRequestException)
        { return new(false, null, "No se pudo conectar con el receptor. Verificá que la URL sea pública, esté disponible y tenga un certificado HTTPS válido."); }
    }
}

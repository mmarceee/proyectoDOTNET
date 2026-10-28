using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://127.0.0.1:5099");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);

var app = builder.Build();
var clavePanel = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var rutaAvisos = "/avisos/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
var estado = new EstadoReceptor();

// El túnel puede exponer todo el servidor. El panel no entrega datos ni permite cambios sin esta clave.
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var recibida = Encoding.UTF8.GetBytes(context.Request.Headers["X-Panel-Clave"].ToString());
        if (!CryptographicOperations.FixedTimeEquals(recibida, Encoding.UTF8.GetBytes(clavePanel)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }
    await next(context);
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/estado", () => Results.Ok(estado.Leer(rutaAvisos)));
app.MapPost("/api/respuesta", (CambiarRespuesta entrada) =>
    entrada.Codigo is 200 or 503
        ? Results.Ok(new { codigo = estado.Cambiar(entrada.Codigo) })
        : Results.BadRequest(new { error = "Sólo se permite responder 200 o 503." }));
app.MapDelete("/api/recibidos", () =>
{
    estado.Limpiar();
    return Results.NoContent();
});
app.MapPost(rutaAvisos, async (HttpRequest request, CancellationToken ct) =>
{
    using var lector = new StreamReader(request.Body, Encoding.UTF8);
    var cuerpo = await lector.ReadToEndAsync(ct);
    var recibido = estado.Registrar(cuerpo, request.Headers["X-Aviso-Fecha"].ToString(),
        request.Headers["X-Aviso-Firma"].ToString());
    // Se registra incluso cuando se responde 503: así se pueden comparar los reintentos.
    return Results.Json(new { recibido = recibido.Numero, codigo = recibido.CodigoRespuesta },
        statusCode: recibido.CodigoRespuesta);
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine("\nRECEPTOR DE DEMOSTRACIÓN (datos en memoria)");
    foreach (var direccion in app.Urls)
        Console.WriteLine($"Panel: {direccion}/#clave={clavePanel}");
    Console.WriteLine($"Ruta para el túnel HTTPS: {rutaAvisos}");
    Console.WriteLine("La clave del panel permite ver los avisos y cambiar la respuesta. No compartirla.\n");
});
app.Run();

internal sealed record CambiarRespuesta(int Codigo);
internal sealed record AvisoRecibido(long Numero, DateTimeOffset Fecha, int CodigoRespuesta,
    string Cuerpo, string FechaFirma, string Firma);

internal sealed class EstadoReceptor
{
    private readonly object bloqueo = new();
    private readonly Queue<AvisoRecibido> recibidos = new();
    private int codigo = 200;
    private long total;

    public object Leer(string rutaAvisos)
    {
        lock (bloqueo)
            return new { codigo, rutaAvisos, total, recibidos = recibidos.Reverse().ToArray() };
    }

    public int Cambiar(int nuevo)
    {
        lock (bloqueo) return codigo = nuevo;
    }

    public AvisoRecibido Registrar(string cuerpo, string fechaFirma, string firma)
    {
        lock (bloqueo)
        {
            var aviso = new AvisoRecibido(++total, DateTimeOffset.UtcNow, codigo, cuerpo, fechaFirma, firma);
            recibidos.Enqueue(aviso);
            if (recibidos.Count > 100) recibidos.Dequeue();
            return aviso;
        }
    }

    public void Limpiar()
    {
        lock (bloqueo)
        {
            recibidos.Clear();
            total = 0;
        }
    }
}

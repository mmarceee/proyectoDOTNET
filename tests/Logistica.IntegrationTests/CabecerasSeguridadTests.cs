namespace Logistica.IntegrationTests;

// Letra 6.3: cabeceras seguras en todas las respuestas, también en una página, en la API y en un error.
public class CabecerasSeguridadTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Theory]
    [InlineData("/cuenta/ingresar")]
    [InlineData("/portal/ingresar")]
    [InlineData("/api/no-existe")]
    public async Task Toda_respuesta_lleva_las_cabeceras_seguras(string ruta)
    {
        using var client = factory.CreateClient();

        var respuesta = await client.GetAsync(ruta);

        Assert.Equal("nosniff", Cabecera(respuesta, "X-Content-Type-Options"));
        Assert.Equal("DENY", Cabecera(respuesta, "X-Frame-Options"));
        Assert.Equal("strict-origin-when-cross-origin", Cabecera(respuesta, "Referrer-Policy"));
        Assert.Contains("geolocation=(self)", Cabecera(respuesta, "Permissions-Policy"));
        Assert.Equal("object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'",
            Cabecera(respuesta, "Content-Security-Policy"));
    }

    private static string Cabecera(HttpResponseMessage respuesta, string nombre) =>
        respuesta.Headers.TryGetValues(nombre, out var valores) ? string.Join(",", valores)
        : respuesta.Content.Headers.TryGetValues(nombre, out var deContenido) ? string.Join(",", deContenido)
        : "";
}

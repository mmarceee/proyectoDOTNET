using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Avisos;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Seguimiento;

public sealed class SuscripcionesAvisoApiTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Ruta = "/api/avisos/suscripciones";
    private static readonly string Clave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly Guid Operador = DatosIniciales.OperadorDemoId;
    private static readonly Guid Comercio = DatosIniciales.ComercioDemoId;
    private static CrearSuscripcionAvisoRequest Solicitud() =>
        new($"https://comercio.example/avisos/{Guid.NewGuid():N}", ["CambioDeEstado", "EntregaFallida", "Devolucion"]);

    [Fact]
    public async Task Flujo_http_crea_consulta_regenera_y_desactiva_sin_exponer_secretos_en_consultas()
    {
        using var api = CrearApi();
        using var client = api.CreateClient(Sesion.Opciones);
        await IngresarAsync(client);
        var request = Solicitud();
        using var creacion = await client.PostAsJsonAsync(Ruta, new
        {
            request.UrlDestino, request.TiposEvento,
            OperadorId = Guid.NewGuid(), ComercioId = Guid.NewGuid(), RelacionComercialId = Guid.NewGuid(),
            SecretoFirma = "secreto-del-cliente", Activa = false
        });
        Assert.Equal(HttpStatusCode.Created, creacion.StatusCode);
        Assert.True(creacion.Headers.CacheControl?.NoStore);
        var creada = (await creacion.Content.ReadFromJsonAsync<SecretoSuscripcionAvisoResponse>())!;
        Assert.Equal($"{Ruta}/{creada.Id}", creacion.Headers.Location!.ToString());
        Assert.Equal(32, Convert.FromBase64String(creada.SecretoFirma).Length);

        using (var detalle = await client.GetAsync(creacion.Headers.Location))
        {
            detalle.EnsureSuccessStatusCode();
            Assert.True(detalle.Headers.CacheControl?.NoStore);
            var vista = (await detalle.Content.ReadFromJsonAsync<SuscripcionAvisoResponse>())!;
            Assert.True(vista.Activa);
            Assert.Equal(request.UrlDestino, vista.UrlDestino);
            Assert.Equal(request.TiposEvento, vista.TiposEvento);
            var json = await detalle.Content.ReadAsStringAsync();
            Assert.DoesNotContain("secreto", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(creada.SecretoFirma, json);
        }

        using (var lista = await client.GetAsync($"{Ruta}?comercioId={Guid.NewGuid()}&operadorId={Guid.NewGuid()}"))
        {
            lista.EnsureSuccessStatusCode();
            Assert.True(lista.Headers.CacheControl?.NoStore);
            Assert.Contains((await lista.Content.ReadFromJsonAsync<List<SuscripcionAvisoResponse>>())!, s => s.Id == creada.Id);
            Assert.DoesNotContain("secreto", await lista.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        using var regeneracion = await client.PostAsync($"{Ruta}/{creada.Id}/secreto", null);
        regeneracion.EnsureSuccessStatusCode();
        Assert.True(regeneracion.Headers.CacheControl?.NoStore);
        var regenerada = (await regeneracion.Content.ReadFromJsonAsync<SecretoSuscripcionAvisoResponse>())!;
        Assert.NotEqual(creada.SecretoFirma, regenerada.SecretoFirma);
        for (var i = 0; i < 2; i++)
        {
            using var baja = await client.PostAsync($"{Ruta}/{creada.Id}/desactivacion", null);
            Assert.Equal(HttpStatusCode.NoContent, baja.StatusCode);
        }
        Assert.False((await client.GetFromJsonAsync<SuscripcionAvisoResponse>($"{Ruta}/{creada.Id}"))!.Activa);

        using var scope = api.Services.CreateScope();
        await using var db = Contexto(scope, Operador, Comercio);
        var guardada = await db.SuscripcionesAviso.SingleAsync(s => s.Id == creada.Id);
        Assert.NotEqual(creada.SecretoFirma, guardada.SecretoFirma);
        Assert.Equal(regenerada.SecretoFirma, new SecretosAviso(Clave).Descifrar(guardada.SecretoFirma, Operador, Comercio));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sin_sesion_todos_los_endpoints_responden_401_incluso_con_respaldo_provisorio(bool respaldo)
    {
        using var api = CrearApi(respaldo: respaldo);
        using var client = api.CreateClient(Sesion.Opciones);
        await VerificarAccesoAsync(client, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Personal_del_operador_y_comercio_sin_operador_no_pueden_usar_los_endpoints()
    {
        using var api = CrearApi();
        using var personal = api.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(personal, "admin@operador-demo.uy");
        await VerificarAccesoAsync(personal, HttpStatusCode.Forbidden);
        using var sinOperador = api.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(sinOperador, "comercio@tiendademo.uy");
        await VerificarAccesoAsync(sinOperador, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Aisla_otro_comercio_y_otro_operador_y_responde_404_como_para_un_id_inexistente()
    {
        using var api = CrearApi();
        using var client = api.CreateClient(Sesion.Opciones);
        await IngresarAsync(client);
        var propia = await CrearAsync(client);
        var ajena = SuscripcionAviso.Crear(Operador, Guid.NewGuid(), Guid.NewGuid(), "https://otra.example/avisos",
            [TipoEventoAviso.Devolucion], "secreto-cifrado-ajeno");
        using (var scope = api.Services.CreateScope())
        {
            await using var db = Contexto(scope, ajena.OperadorId, ajena.ComercioId);
            db.SuscripcionesAviso.Add(ajena);
            await db.SaveChangesAsync();
        }
        Assert.DoesNotContain((await client.GetFromJsonAsync<List<SuscripcionAvisoResponse>>(Ruta))!, s => s.Id == ajena.Id);
        foreach (var id in new[] { ajena.Id, Guid.NewGuid() }) await VerificarNoEncontradaAsync(client, id);

        await Sesion.ElegirOperadorAsync(client, DatosIniciales.OperadorEsteId);
        await TokenAsync(client);
        Assert.DoesNotContain((await client.GetFromJsonAsync<List<SuscripcionAvisoResponse>>(Ruta))!, s => s.Id == propia.Id);
        await VerificarNoEncontradaAsync(client, propia.Id);
        await Sesion.ElegirOperadorAsync(client, Operador);
        await TokenAsync(client);
        Assert.True((await client.GetFromJsonAsync<SuscripcionAvisoResponse>($"{Ruta}/{propia.Id}"))!.Activa);
    }

    [Fact]
    public async Task Escrituras_sin_token_o_con_token_invalido_no_cambian_la_suscripcion()
    {
        using var api = CrearApi();
        using var client = api.CreateClient(Sesion.Opciones);
        await IngresarAsync(client);
        var creada = await CrearAsync(client);
        foreach (var token in new string?[] { null, "token-invalido" })
        {
            client.DefaultRequestHeaders.Remove("RequestVerificationToken");
            if (token is not null) client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
            using var alta = await client.PostAsJsonAsync(Ruta, Solicitud());
            Assert.Equal(HttpStatusCode.BadRequest, alta.StatusCode);
            foreach (var accion in new[] { "desactivacion", "secreto", "prueba" })
            {
                using var respuesta = await client.PostAsync($"{Ruta}/{creada.Id}/{accion}", null);
                Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            }
        }
        using var scope = api.Services.CreateScope();
        await using var db = Contexto(scope, Operador, Comercio);
        var actual = await db.SuscripcionesAviso.SingleAsync(s => s.Id == creada.Id);
        Assert.True(actual.Activa);
        Assert.Equal(creada.SecretoFirma, new SecretosAviso(Clave).Descifrar(actual.SecretoFirma, Operador, Comercio));
    }

    [Fact]
    public async Task Cuerpos_invalidos_responden_400_y_no_crean_suscripciones()
    {
        using var api = CrearApi();
        using var client = api.CreateClient(Sesion.Opciones);
        await IngresarAsync(client);
        using var scope = api.Services.CreateScope();
        await using var db = Contexto(scope, Operador, Comercio);
        var cantidad = await db.SuscripcionesAviso.CountAsync();
        foreach (var json in new[]
        {
            "{}",
            "{\"urlDestino\":null,\"tiposEvento\":[\"CambioDeEstado\"]}",
            "{\"urlDestino\":\"https://comercio.example/avisos\",\"tiposEvento\":null}",
            "{\"urlDestino\":\"https://comercio.example/avisos\",\"tiposEvento\":[]}",
            "{\"urlDestino\":\"http://comercio.example/avisos\",\"tiposEvento\":[\"CambioDeEstado\"]}",
            "{\"urlDestino\":\"https://comercio.example/avisos\",\"tiposEvento\":[\"Desconocido\"]}",
            "{\"urlDestino\":\"https://comercio.example/avisos\",\"tiposEvento\":[\"0\"]}",
            "{\"urlDestino\":\"https://comercio.example/avisos\",\"tiposEvento\":[null]}"
        })
        {
            using var respuesta = await client.PostAsync(Ruta, new StringContent(json, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        }
        Assert.Equal(cantidad, await db.SuscripcionesAviso.CountAsync());
    }

    [Fact]
    public async Task Consultar_y_desactivar_funcionan_sin_necesitar_la_clave_de_cifrado()
    {
        using var api = CrearApi();
        using var client = api.CreateClient(Sesion.Opciones);
        await IngresarAsync(client);
        var creada = await CrearAsync(client);
        using var sinClave = CrearApi(clave: "");
        using var lector = sinClave.CreateClient(Sesion.Opciones);
        await IngresarAsync(lector);
        Assert.NotNull(await lector.GetFromJsonAsync<SuscripcionAvisoResponse>($"{Ruta}/{creada.Id}"));
        Assert.Contains((await lector.GetFromJsonAsync<List<SuscripcionAvisoResponse>>(Ruta))!, s => s.Id == creada.Id);
        using var baja = await lector.PostAsync($"{Ruta}/{creada.Id}/desactivacion", null);
        Assert.Equal(HttpStatusCode.NoContent, baja.StatusCode);
    }

    private WebApplicationFactory<Program> CrearApi(bool respaldo = false, string? clave = null) =>
        factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", respaldo.ToString())
            .UseSetting("Seguimiento:Avisos:ClaveCifrado", clave ?? Clave));

    private static async Task IngresarAsync(HttpClient client)
    {
        await Sesion.IngresarAsync(client, "comercio@tiendademo.uy");
        await Sesion.ElegirOperadorAsync(client, Operador);
        await TokenAsync(client);
    }

    private static async Task TokenAsync(HttpClient client)
    {
        using var respuesta = await client.GetAsync($"{Ruta}/antifalsificacion");
        respuesta.EnsureSuccessStatusCode();
        Assert.True(respuesta.Headers.CacheControl?.NoStore);
        var token = (await respuesta.Content.ReadFromJsonAsync<TokenAntifalsificacionResponse>())!.Token;
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
    }

    private static async Task<SecretoSuscripcionAvisoResponse> CrearAsync(HttpClient client)
    {
        using var respuesta = await client.PostAsJsonAsync(Ruta, Solicitud());
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<SecretoSuscripcionAvisoResponse>())!;
    }

    private static async Task VerificarAccesoAsync(HttpClient client, HttpStatusCode esperado)
    {
        foreach (var ruta in new[] { Ruta, $"{Ruta}/{Guid.NewGuid()}", $"{Ruta}/antifalsificacion" })
        {
            using var respuesta = await client.GetAsync(ruta);
            Assert.Equal(esperado, respuesta.StatusCode);
        }
        using var alta = await client.PostAsJsonAsync(Ruta, Solicitud());
        Assert.Equal(esperado, alta.StatusCode);
        foreach (var accion in new[] { "desactivacion", "secreto", "prueba" })
        {
            using var respuesta = await client.PostAsync($"{Ruta}/{Guid.NewGuid()}/{accion}", null);
            Assert.Equal(esperado, respuesta.StatusCode);
        }
    }

    private static async Task VerificarNoEncontradaAsync(HttpClient client, Guid id)
    {
        using var detalle = await client.GetAsync($"{Ruta}/{id}");
        Assert.Equal(HttpStatusCode.NotFound, detalle.StatusCode);
        foreach (var accion in new[] { "desactivacion", "secreto", "prueba" })
        {
            using var respuesta = await client.PostAsync($"{Ruta}/{id}/{accion}", null);
            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        }
    }

    private static SeguimientoDbContext Contexto(IServiceScope scope, Guid operador, Guid comercio) =>
        ActivatorUtilities.CreateInstance<SeguimientoDbContext>(scope.ServiceProvider, new InquilinoFijo(operador, comercio));
}

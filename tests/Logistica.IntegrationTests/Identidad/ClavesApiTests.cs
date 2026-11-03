using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.ClavesApi;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Domain.Comercios;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Identidad;

public sealed class ClavesApiTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Ruta = "/api/portal/claves-api";
    private static readonly Guid Operador = DatosIniciales.OperadorDemoId;
    private static readonly Guid ComercioDemo = DatosIniciales.ComercioDemoId;

    [Fact]
    public async Task Genera_los_dos_tipos_guarda_solo_el_hash_y_revoca_sin_cambiar_la_fecha_en_un_reintento()
    {
        using var client = factory.CreateClient(Sesion.Opciones);
        await IngresarAsync(client);
        var prueba = await CrearAsync(client, " Prueba tienda ", "Prueba");
        var produccion = await CrearAsync(client, "Tienda", "Produccion");
        Assert.StartsWith("log_prueba_", prueba.Secreto);
        Assert.StartsWith("log_prod_", produccion.Secreto);
        Assert.StartsWith(prueba.Clave.Prefijo, prueba.Secreto);
        Assert.Equal("Prueba tienda", prueba.Clave.Nombre);
        Assert.Null(prueba.Clave.UltimoUsoEn);

        using var scope = factory.Services.CreateScope();
        await using var db = Contexto(scope, ComercioDemo);
        var guardada = await db.ClavesApi.AsNoTracking().SingleAsync(c => c.Id == prueba.Clave.Id);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prueba.Secreto))), guardada.Hash);
        Assert.Equal(Operador, guardada.OperadorId);
        Assert.Equal(ComercioDemo, guardada.ComercioId);
        Assert.Equal(await db.RelacionesComerciales.Select(r => r.Id).SingleAsync(), guardada.RelacionComercialId);

        using var primeraRevocacion = await client.PostAsync($"{Ruta}/{prueba.Clave.Id}/revocacion", null);
        Assert.Equal(HttpStatusCode.NoContent, primeraRevocacion.StatusCode);
        var revocada = (await ListarAsync(client)).Single(c => c.Id == prueba.Clave.Id);
        Assert.NotNull(revocada.RevocadaEn);
        using var repetida = await client.PostAsync($"{Ruta}/{prueba.Clave.Id}/revocacion", null);
        Assert.Equal(HttpStatusCode.NoContent, repetida.StatusCode);

        using var lista = await client.GetAsync(Ruta);
        var json = await lista.Content.ReadAsStringAsync();
        Assert.DoesNotContain(prueba.Secreto, json);
        Assert.DoesNotContain(produccion.Secreto, json);
        Assert.DoesNotContain(guardada.Hash, json);
        Assert.True(lista.Headers.CacheControl?.NoStore);
        Assert.Equal(revocada.RevocadaEn, (await ListarAsync(client)).Single(c => c.Id == prueba.Clave.Id).RevocadaEn);
    }

    [Fact]
    public async Task No_lista_ni_revoca_claves_de_otro_comercio_o_de_otro_operador()
    {
        using var client = factory.CreateClient(Sesion.Opciones);
        await IngresarAsync(client);
        var propia = await CrearAsync(client, "Propia", "Prueba");
        var ajena = await CrearClaveDeOtroComercioAsync();
        Assert.DoesNotContain(await ListarAsync(client), c => c.Id == ajena);
        using var revocacionAjena = await client.PostAsync($"{Ruta}/{ajena}/revocacion", null);
        Assert.Equal(HttpStatusCode.NotFound, revocacionAjena.StatusCode);

        using var cambio = await Sesion.ElegirOperadorAsync(client, DatosIniciales.OperadorEsteId);
        Assert.Equal(HttpStatusCode.Redirect, cambio.StatusCode);
        await RenovarTokenAsync(client);
        Assert.DoesNotContain(await ListarAsync(client), c => c.Id == propia.Clave.Id);
        using var revocacionOtroOperador = await client.PostAsync($"{Ruta}/{propia.Clave.Id}/revocacion", null);
        Assert.Equal(HttpStatusCode.NotFound, revocacionOtroOperador.StatusCode);
    }

    [Fact]
    public async Task Exige_cookie_operador_y_antifalsificacion_y_valida_la_solicitud()
    {
        using var client = factory.CreateClient(Sesion.Opciones);
        using var sinSesion = await client.GetAsync(Ruta);
        Assert.Equal(HttpStatusCode.Unauthorized, sinSesion.StatusCode);

        using var ingreso = await Sesion.IngresarAsync(client, "comercio@tiendademo.uy");
        using var sinOperador = await client.GetAsync(Ruta);
        Assert.Equal(HttpStatusCode.Forbidden, sinOperador.StatusCode);
        using var eleccion = await Sesion.ElegirOperadorAsync(client, Operador);
        Assert.Equal(HttpStatusCode.Redirect, eleccion.StatusCode);

        using var sinToken = await client.PostAsJsonAsync(Ruta, new CrearClaveApiRequest("Tienda", "Prueba"));
        Assert.Equal(HttpStatusCode.BadRequest, sinToken.StatusCode);
        await RenovarTokenAsync(client);
        using var nombreVacio = await client.PostAsJsonAsync(Ruta, new CrearClaveApiRequest(" ", "Prueba"));
        Assert.Equal(HttpStatusCode.BadRequest, nombreVacio.StatusCode);
        using var tipoInvalido = await client.PostAsJsonAsync(Ruta, new CrearClaveApiRequest("Tienda", "0"));
        Assert.Equal(HttpStatusCode.BadRequest, tipoInvalido.StatusCode);
    }

    private async Task<Guid> CrearClaveDeOtroComercioAsync()
    {
        var comercio = Comercio.Crear("Otro comercio", "210000000025");
        using var scope = factory.Services.CreateScope();
        await using var db = Contexto(scope, comercio.Id);
        var ahora = DateTimeOffset.UtcNow;
        var relacion = RelacionComercial.Crear(Operador, comercio.Id, "otro@example.test", ahora);
        var hash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var clave = ClaveApi.Crear(relacion, "Ajena", TipoClaveApi.Prueba, hash, "log_prueba_xxxxx", ahora);
        db.Comercios.Add(comercio);
        db.RelacionesComerciales.Add(relacion);
        db.ClavesApi.Add(clave);
        await db.SaveChangesAsync();
        return clave.Id;
    }

    private static AdministracionDbContext Contexto(IServiceScope scope, Guid comercio) =>
        ActivatorUtilities.CreateInstance<AdministracionDbContext>(scope.ServiceProvider, new InquilinoFijo(Operador, comercio));

    private static async Task IngresarAsync(HttpClient client)
    {
        using var ingreso = await Sesion.IngresarAsync(client, "comercio@tiendademo.uy");
        using var eleccion = await Sesion.ElegirOperadorAsync(client, Operador);
        Assert.Equal(HttpStatusCode.Redirect, eleccion.StatusCode);
        await RenovarTokenAsync(client);
    }

    private static async Task RenovarTokenAsync(HttpClient client)
    {
        var token = (await client.GetFromJsonAsync<TokenClaveApiResponse>($"{Ruta}/antifalsificacion"))!.Token;
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
    }

    private static async Task<ClaveApiCreadaResponse> CrearAsync(HttpClient client, string nombre, string tipo)
    {
        using var respuesta = await client.PostAsJsonAsync(Ruta, new CrearClaveApiRequest(nombre, tipo));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.True(respuesta.Headers.CacheControl?.NoStore);
        return (await respuesta.Content.ReadFromJsonAsync<ClaveApiCreadaResponse>())!;
    }

    private static async Task<ClaveApiResponse[]> ListarAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ClaveApiResponse[]>(Ruta))!;
}

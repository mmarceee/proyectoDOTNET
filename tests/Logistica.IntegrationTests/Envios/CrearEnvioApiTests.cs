using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.ClavesApi;
using Logistica.Http.Contracts.Envios;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.Modules.Seguimiento.Infrastructure.Messaging;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Envios;

public sealed class CrearEnvioApiTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Ruta = "/api/v1/envios";
    private const string Gestion = "/api/portal/claves-api";
    private const string Cabecera = "X-Api-Key";
    private static readonly Guid Operador = DatosIniciales.OperadorDemoId;
    private static readonly Guid Comercio = DatosIniciales.ComercioDemoId;

    [Fact]
    public async Task Clave_valida_crea_envio_actualiza_uso_y_la_revocacion_corta_el_acceso()
    {
        using var api = Api();
        using var portal = await PortalAsync(api);
        var clave = await CrearClaveAsync(portal, "Produccion");
        using var integracion = Integracion(api, clave.Secreto);
        var envio = Solicitud();
        using var soloCookie = await portal.PostAsJsonAsync(Ruta, envio);
        Assert.Equal(HttpStatusCode.Unauthorized, soloCookie.StatusCode);
        using var soloClave = await integracion.GetAsync(Gestion);
        Assert.Equal(HttpStatusCode.Unauthorized, soloClave.StatusCode);

        var alta = await AltaAsync(integracion, envio, HttpStatusCode.Created);
        Assert.StartsWith("/seguimiento/", alta.EnlaceSeguimiento);
        Assert.Equal("Creado", alta.Estado);
        Assert.Equal(150m, alta.Tarifa);
        var claves = (await portal.GetFromJsonAsync<ClaveApiResponse[]>(Gestion))!;
        Assert.NotNull(claves.Single(c => c.Id == clave.Clave.Id).UltimoUsoEn);

        using var revocacion = await portal.PostAsync($"{Gestion}/{clave.Clave.Id}/revocacion", null);
        Assert.Equal(HttpStatusCode.NoContent, revocacion.StatusCode);
        using var revocada = await integracion.PostAsJsonAsync(Ruta, envio);
        Assert.Equal(HttpStatusCode.Unauthorized, revocada.StatusCode);
    }

    [Fact]
    public async Task Altas_concurrentes_crean_un_envio_y_un_evento_por_ambiente()
    {
        using var api = Api();
        using var portal = await PortalAsync(api);
        using var produccion = Integracion(api, (await CrearClaveAsync(portal, "Produccion")).Secreto);
        using var pruebas = Integracion(api, (await CrearClaveAsync(portal, "Prueba")).Secreto);
        var request = Solicitud();
        var altas = await Task.WhenAll(produccion.PostAsJsonAsync(Ruta, request), produccion.PostAsJsonAsync(Ruta, request));
        using var primera = altas[0];
        using var segunda = altas[1];
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Created }, altas.Select(r => r.StatusCode).Order());
        var real = (await primera.Content.ReadFromJsonAsync<AltaEnvioApiResponse>())!;
        Assert.Equal(real, await segunda.Content.ReadFromJsonAsync<AltaEnvioApiResponse>());

        var prueba = await AltaAsync(pruebas, request, HttpStatusCode.Created);
        Assert.NotEqual(real.Numero, prueba.Numero);
        Assert.Equal(prueba, await AltaAsync(pruebas, request, HttpStatusCode.OK));
        Assert.Equal(prueba, await pruebas.GetFromJsonAsync<AltaEnvioApiResponse>(prueba.EnlaceSeguimiento));
        using var enlaceProduccion = await produccion.GetAsync(prueba.EnlaceSeguimiento);
        Assert.Equal(HttpStatusCode.NotFound, enlaceProduccion.StatusCode);
        using var otroOperador = await PortalAsync(api, DatosIniciales.OperadorEsteId);
        using var pruebaAjena = Integracion(api, (await CrearClaveAsync(otroOperador, "Prueba")).Secreto);
        using var enlaceAjeno = await pruebaAjena.GetAsync(prueba.EnlaceSeguimiento);
        Assert.Equal(HttpStatusCode.NotFound, enlaceAjeno.StatusCode);

        await using var scope = api.Services.CreateAsyncScope();
        await using var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(
            scope.ServiceProvider, new InquilinoFijo(Operador, Comercio));
        var filas = await db.Envios.IgnoreQueryFilters().Include(e => e.Eventos)
            .Where(e => e.ReferenciaComercio == request.ReferenciaComercio).ToListAsync();
        Assert.Equal(2, filas.Count);
        Assert.All(filas, e => Assert.Equal(OrigenEvento.Api, Assert.Single(e.Eventos).Origen));
        Assert.Equal(real.Numero, (await db.Envios.SingleAsync(e => e.ReferenciaComercio == request.ReferenciaComercio)).Numero);
        Assert.Equal(prueba.Numero, filas.Single(e => e.EsPrueba).Numero);

        var eventos = (await db.Outbox.AsNoTracking().Where(e => e.Tipo == EnvioAdmitido.Tipo).ToListAsync())
            .Select(e => JsonSerializer.Deserialize<EnvioAdmitido>(e.Contenido)!)
            .Where(e => e.Numero == real.Numero || e.Numero == prueba.Numero).ToList();
        Assert.Equal(2, eventos.Count);
        Assert.False(eventos.Single(e => e.Numero == real.Numero).EsPrueba);
        Assert.True(eventos.Single(e => e.Numero == prueba.Numero).EsPrueba);
    }

    [Fact]
    public async Task El_evento_de_prueba_prepara_el_aviso_y_omite_el_correo()
    {
        using var api = Api();
        using var portal = await PortalAsync(api);
        using var pruebas = Integracion(api, (await CrearClaveAsync(portal, "Prueba")).Secreto);
        var envio = await AltaAsync(pruebas, Solicitud(), HttpStatusCode.Created);
        await using var scope = api.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var tenant = new InquilinoFijo(Operador, Comercio);
        await using var envios = ActivatorUtilities.CreateInstance<EnviosDbContext>(sp, tenant);
        await using var administracion = ActivatorUtilities.CreateInstance<AdministracionDbContext>(sp, tenant);
        await using var seguimiento = ActivatorUtilities.CreateInstance<SeguimientoDbContext>(sp, tenant);
        var relacion = await administracion.RelacionesComerciales.Select(r => r.Id).SingleAsync();
        seguimiento.SuscripcionesAviso.Add(SuscripcionAviso.Crear(Operador, Comercio, relacion,
            "https://comercio.example/avisos", [TipoEventoAviso.CambioDeEstado], "cifrado-de-prueba"));
        await seguimiento.SaveChangesAsync();
        var outbox = (await envios.Outbox.AsNoTracking().Where(m => m.Tipo == EnvioAdmitido.Tipo).ToListAsync())
            .Single(m => JsonSerializer.Deserialize<EnvioAdmitido>(m.Contenido)!.Numero == envio.Numero);
        Assert.Null(JsonSerializer.Deserialize<EnvioAdmitido>(outbox.Contenido)!.EmailDestinatario);
        var mensaje = new MensajeIntegracion(outbox.Id, outbox.Tipo, outbox.OperadorId, outbox.ComercioId,
            outbox.OcurridoEn, outbox.TraceParent, outbox.Contenido);

        await ProcesarAsync(api.Services, PrepararAvisos.Cola(mensaje.Tipo), mensaje);
        await ProcesarAsync(api.Services, NotificarDestinatario.Cola(mensaje.Tipo), mensaje);
        var aviso = await seguimiento.EntregasAviso.SingleAsync(e => e.EventoId == mensaje.Id);
        Assert.True(aviso.EsPrueba);
        Assert.False(await seguimiento.Notificaciones.AnyAsync(n => n.EnvioId == aviso.EnvioId));
    }

    [Fact]
    public async Task Diferencia_errores_de_validacion_cobertura_y_tarifa()
    {
        using var api = Api(b => b.ComoProduccion());
        using var portal = await PortalAsync(api);
        using var client = Integracion(api, (await CrearClaveAsync(portal, "Prueba")).Secreto);
        var request = Solicitud();
        foreach (var (cuerpo, estado) in new[]
        {
            (request with { ReferenciaComercio = " " }, HttpStatusCode.BadRequest),
            (request with { Bultos = [] }, HttpStatusCode.BadRequest),
            (request with { Direccion = request.Direccion with { CodigoPostal = "00000" } }, HttpStatusCode.UnprocessableEntity),
            (request with { Bultos = [new BultoRequest(10000, 30, 20, 10)] }, HttpStatusCode.UnprocessableEntity),
        })
        {
            using var respuesta = await client.PostAsJsonAsync(Ruta, cuerpo);
            Assert.Equal(estado, respuesta.StatusCode);
            Assert.NotNull((await respuesta.Content.ReadFromJsonAsync<ProblemDetails>())?.Detail);
        }

        using var jsonInvalido = new StringContent("{", Encoding.UTF8, "application/json");
        using var malformado = await client.PostAsync(Ruta, jsonInvalido);
        Assert.Equal(HttpStatusCode.BadRequest, malformado.StatusCode);
        Assert.Equal(400, (await malformado.Content.ReadFromJsonAsync<ProblemDetails>())?.Status);
    }

    [Fact]
    public async Task Separa_cuotas_y_limita_intentos_con_clave_invalida()
    {
        using var api = Api(b => b.UseSetting("ApiPublica:PruebaPorMinuto", "1")
            .UseSetting("ApiPublica:ProduccionPorMinuto", "2")
            .UseSetting("ApiPublica:SolicitudesPorIpPorMinuto", "6"));
        using var portal = await PortalAsync(api);
        using var pruebas = Integracion(api, (await CrearClaveAsync(portal, "Prueba")).Secreto);
        using var produccion = Integracion(api, (await CrearClaveAsync(portal, "Produccion")).Secreto);
        using var invalida = Integracion(api, "no-existe");
        await VerificarAsync(pruebas, HttpStatusCode.Created);
        await VerificarAsync(pruebas, HttpStatusCode.TooManyRequests);
        await VerificarAsync(produccion, HttpStatusCode.Created);
        await VerificarAsync(produccion, HttpStatusCode.Created);
        await VerificarAsync(produccion, HttpStatusCode.TooManyRequests);
        await VerificarAsync(invalida, HttpStatusCode.Unauthorized);
        await VerificarAsync(invalida, HttpStatusCode.TooManyRequests);
    }

    private static async Task VerificarAsync(HttpClient client, HttpStatusCode esperado)
    {
        using var respuesta = await client.PostAsJsonAsync(Ruta, Solicitud());
        Assert.Equal(esperado, respuesta.StatusCode);
        if (esperado == HttpStatusCode.TooManyRequests)
            Assert.True(respuesta.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    private static async Task ProcesarAsync(IServiceProvider servicios, string cola, MensajeIntegracion mensaje)
    {
        await using var scope = servicios.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<InquilinoMensaje>().Fijar(mensaje);
        await scope.ServiceProvider.GetRequiredKeyedService<IConsumidorIntegracion>(cola).ProcesarAsync(mensaje, default);
    }

    private WebApplicationFactory<Program> Api(Action<IWebHostBuilder>? configurar = null) => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Seguimiento:ClaveToken", Convert.ToBase64String(new byte[32]));
        configurar?.Invoke(b);
    });

    internal static async Task<HttpClient> PortalAsync(WebApplicationFactory<Program> api, Guid? operador = null)
    {
        var client = api.CreateClient(Sesion.Opciones);
        using var ingreso = await Sesion.IngresarAsync(client, "comercio@tiendademo.uy");
        using var eleccion = await Sesion.ElegirOperadorAsync(client, operador ?? Operador);
        Assert.Equal(HttpStatusCode.Redirect, eleccion.StatusCode);
        var token = (await client.GetFromJsonAsync<TokenClaveApiResponse>($"{Gestion}/antifalsificacion"))!.Token;
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        return client;
    }

    internal static async Task<ClaveApiCreadaResponse> CrearClaveAsync(HttpClient portal, string tipo)
    {
        using var respuesta = await portal.PostAsJsonAsync(Gestion, new CrearClaveApiRequest("Integración CU-81", tipo));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<ClaveApiCreadaResponse>())!;
    }

    private static HttpClient Integracion(WebApplicationFactory<Program> api, string secreto)
    {
        var client = api.CreateClient(Sesion.Opciones);
        client.DefaultRequestHeaders.Add(Cabecera, secreto);
        return client;
    }

    private static async Task<AltaEnvioApiResponse> AltaAsync(HttpClient client, CrearEnvioApiRequest request, HttpStatusCode esperado)
    {
        using var respuesta = await client.PostAsJsonAsync(Ruta, request);
        Assert.Equal(esperado, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<AltaEnvioApiResponse>())!;
    }

    private static CrearEnvioApiRequest Solicitud() => new(
        Guid.NewGuid().ToString("N"),
        new("Ana Pérez", "099123456", "ana@example.test", null),
        new("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", null),
        [new(2, 30, 20, 10)]);
}

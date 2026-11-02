using System.Net;
using System.Text.RegularExpressions;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Administracion;

// CU-08 de punta a punta, con sesiones reales y sin el respaldo provisorio. Da de alta repartidores y vehículos del
// operador Demo, así que tiene su propia base; cada prueba usa documentos y matrículas propios.
public class GestionarFlotaTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Flota = "/backoffice/flota";

    private HttpClient Cliente() => factory
        .WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
        .CreateClient(Sesion.Opciones);

    // Flujo principal y aislamiento (ADR-0002): lo que da de alta Demo no lo ve el administrador de Este, ni en el
    // listado ni por su dirección (404, como si no existiera).
    [Fact]
    public async Task El_administrador_da_de_alta_repartidor_y_vehiculo_y_otro_operador_no_los_ve()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");
        using var este = await AdminAsync("admin@envios-del-este.uy");

        var repartidor = await CrearRepartidorAsync(demo, "Rita Flota", "F-ALTA");
        var vehiculo = await CrearVehiculoAsync(demo, "FLT-0001");

        var listadoEste = await este.GetStringAsync(Flota);
        Assert.DoesNotContain("F-ALTA", listadoEste);
        Assert.DoesNotContain("FLT-0001", listadoEste);
        Assert.Equal(HttpStatusCode.NotFound, (await este.GetAsync($"{Flota}/repartidores/{repartidor}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await este.GetAsync($"{Flota}/vehiculos/{vehiculo}")).StatusCode);
    }

    [Fact]
    public async Task Solo_el_administrador_gestiona_la_flota()
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "despacho@operador-demo.uy");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Flota)).StatusCode);
    }

    // A1: documento y matrícula únicos en el operador; la matrícula sin distinguir mayúsculas.
    [Fact]
    public async Task Un_documento_o_una_matricula_repetidos_se_indican_y_no_se_crean()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");
        await CrearRepartidorAsync(demo, "Rita", "F-A1");
        await CrearVehiculoAsync(demo, "flt-a1");

        var repartidor = await TextoAsync(await AccionAsync(demo, Flota, "Repartidor",
            [new("Nombre", "Otra"), new("Documento", "F-A1")]));
        var vehiculo = await TextoAsync(await EnviarVehiculoAsync(demo, "FLT-A1", "800"));

        Assert.Contains("Ya existe un repartidor con el documento F-A1.", repartidor);
        Assert.Contains("Ya existe un vehículo con la matrícula FLT-A1.", vehiculo);
    }

    // A2: las capacidades tienen que ser mayores que cero, al crear y al modificar.
    [Fact]
    public async Task Una_capacidad_en_cero_se_rechaza()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");

        var alta = await TextoAsync(await EnviarVehiculoAsync(demo, "FLT-A2", "0"));
        Assert.Contains("El vehículo necesita matrícula, tipo y capacidades positivas.", alta);

        var id = await CrearVehiculoAsync(demo, "FLT-A2-OK");
        var modificar = await TextoAsync(await AccionAsync(demo, $"{Flota}/vehiculos/{id}", "Modificar",
            [new("Tipo", "Furgón"), new("CapacidadPesoKg", "0"), new("CapacidadVolumenM3", "6"),
                new("LargoCargaCm", "300"), new("AnchoCargaCm", "170"), new("AltoCargaCm", "160")]));
        Assert.Contains("El vehículo necesita matrícula, tipo y capacidades positivas.", modificar);
    }

    // A3: con una ruta Planificada no se desactiva; sin rutas, sí, y queda inactivo.
    [Fact]
    public async Task No_se_desactiva_con_una_ruta_sin_finalizar()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");
        var ocupado = await CrearVehiculoAsync(demo, "FLT-A3");
        var libre = await CrearRepartidorAsync(demo, "Libre", "F-A3");
        await RutaPlanificadaAsync(Guid.NewGuid(), ocupado);

        var bloqueado = await TextoAsync(await AccionAsync(demo, $"{Flota}/vehiculos/{ocupado}", "Desactivar", []));
        Assert.Contains("Está asignado a una ruta que todavía no se finalizó.", bloqueado);

        var desactivar = await AccionAsync(demo, $"{Flota}/repartidores/{libre}", "Desactivar", []);
        Assert.Equal(HttpStatusCode.Redirect, desactivar.StatusCode);
        Assert.Contains("Inactivo", await demo.GetStringAsync($"{Flota}/repartidores/{libre}"));
    }

    // Una ruta Planificada de Demo con ese repartidor y ese vehículo, guardada con Demo como inquilino.
    private async Task RutaPlanificadaAsync(Guid repartidorId, Guid vehiculoId)
    {
        var ruta = Ruta.Crear(DatosIniciales.OperadorDemoId, new DateOnly(2035, 3, 10), repartidorId, vehiculoId,
            [new EnvioRuta(Guid.NewGuid(), "ENV-FLOTA", null, null, null, false, [])], DateTimeOffset.UtcNow);

        using var scope = factory.Services.CreateScope();
        await using var db = ActivatorUtilities.CreateInstance<PlanificacionDbContext>(scope.ServiceProvider,
            new InquilinoFijo(DatosIniciales.OperadorDemoId, null));
        db.Rutas.Add(ruta);
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> AdminAsync(string email)
    {
        var client = Cliente();
        await Sesion.IngresarAsync(client, email);
        return client;
    }

    // El alta vuelve al listado: el Id se lee del enlace al detalle.
    private static async Task<Guid> CrearRepartidorAsync(HttpClient client, string nombre, string documento)
    {
        var respuesta = await AccionAsync(client, Flota, "Repartidor", [new("Nombre", nombre), new("Documento", documento)]);
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        return IdEnListado(await client.GetStringAsync(Flota), "repartidores", nombre);
    }

    private static async Task<Guid> CrearVehiculoAsync(HttpClient client, string matricula)
    {
        var respuesta = await EnviarVehiculoAsync(client, matricula, "800");
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        return IdEnListado(await client.GetStringAsync(Flota), "vehiculos", matricula.ToUpperInvariant());
    }

    private static Task<HttpResponseMessage> EnviarVehiculoAsync(HttpClient client, string matricula, string peso)
    {
        return AccionAsync(client, Flota, "Vehiculo",
            [new("Matricula", matricula), new("Tipo", "Furgón"), new("CapacidadPesoKg", peso), new("CapacidadVolumenM3", "6"),
                new("LargoCargaCm", "300"), new("AnchoCargaCm", "170"), new("AltoCargaCm", "160")]);
    }

    private static Guid IdEnListado(string html, string tipo, string texto)
    {
        var enlace = Regex.Match(html, $"href=\"/backoffice/flota/{tipo}/([0-9a-f-]{{36}})\">{Regex.Escape(texto)}<");
        Assert.True(enlace.Success, $"No aparece {texto} en el listado.");
        return Guid.Parse(enlace.Groups[1].Value);
    }

    // Una acción de la página (?handler=...), con el token antifalsificación de la página.
    private static async Task<HttpResponseMessage> AccionAsync(
        HttpClient client, string pagina, string accion, List<KeyValuePair<string, string>> campos)
    {
        campos.Add(new("__RequestVerificationToken", await Sesion.TokenAsync(client, pagina)));
        return await client.PostAsync($"{pagina}?handler={accion}", new FormUrlEncodedContent(campos));
    }

    // Razor codifica las tildes: se decodifica para buscar el texto que ve el usuario.
    private static async Task<string> TextoAsync(HttpResponseMessage respuesta)
    {
        return WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
    }
}

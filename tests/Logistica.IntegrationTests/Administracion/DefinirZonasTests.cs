using System.Net;
using System.Text.RegularExpressions;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Administracion;

// CU-04 de punta a punta, con sesiones reales y sin el respaldo provisorio. Crea zonas del operador Demo, así que tiene
// su propia base; cada prueba usa códigos y códigos postales propios para no depender de las demás.
public class DefinirZonasTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Listado = "/backoffice/zonas";

    private HttpClient Cliente() => factory
        .WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
        .CreateClient(Sesion.Opciones);

    // Flujo principal y aislamiento (ADR-0002): la zona de Demo no la ve el administrador de Este, ni en el listado ni
    // por su dirección (404, como si no existiera).
    [Fact]
    public async Task El_administrador_crea_una_zona_y_otro_operador_no_la_ve()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");
        using var este = await AdminAsync("admin@envios-del-este.uy");

        var id = await CrearAsync(demo, "T-ALTA", "90001, 90002");

        Assert.Contains("T-ALTA", await demo.GetStringAsync(Listado));
        Assert.DoesNotContain("T-ALTA", await este.GetStringAsync(Listado));
        Assert.Equal(HttpStatusCode.NotFound, (await este.GetAsync($"{Listado}/{id}")).StatusCode);
    }

    [Fact]
    public async Task Solo_el_administrador_define_zonas()
    {
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "despacho@operador-demo.uy");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Listado)).StatusCode);
    }

    // A1: el código es único en el operador.
    [Fact]
    public async Task Un_codigo_repetido_se_indica_y_no_se_crea()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");
        await CrearAsync(demo, "T-A1", "90010");

        var html = await TextoAsync(await EnviarAltaAsync(demo, "T-A1", "90011"));

        Assert.Contains("Ya existe una zona con el código T-A1.", html);
    }

    // A2: un código postal está en una sola zona activa. Al desactivar la zona (A5), el código queda libre.
    [Fact]
    public async Task Un_codigo_postal_de_otra_zona_activa_se_indica_y_se_libera_al_desactivarla()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");

        var html = await TextoAsync(await EnviarAltaAsync(demo, "T-A2", "11000"));
        Assert.Contains("El código postal 11000 ya pertenece a la zona MVD-DEMO.", html);

        var id = await CrearAsync(demo, "T-A2-VIEJA", "90020");
        var desactivar = await AccionAsync(demo, id, "Desactivar", []);
        Assert.Equal(HttpStatusCode.Redirect, desactivar.StatusCode);

        await CrearAsync(demo, "T-A2-NUEVA", "90020");
    }

    // Paso 4, A3 y A4: se agrega una franja; otra que se superpone el mismo día se rechaza; y el cambio con fecha deja
    // la anterior vigente hasta el día antes, en el historial.
    [Fact]
    public async Task Franjas_se_agregan_no_se_superponen_y_se_reemplazan_con_fecha()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");
        var id = await CrearAsync(demo, "T-FRANJAS", "90030");

        var agregar = await FranjaAsync(demo, id, "09:00", "13:00", ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"], "2035-01-01");
        Assert.Equal(HttpStatusCode.Redirect, agregar.StatusCode);

        var superpuesta = await TextoAsync(await FranjaAsync(demo, id, "12:00", "16:00", ["Monday"], "2035-01-01"));
        Assert.Contains("La franja se superpone con otra de la zona en el mismo día.", superpuesta);

        var detalle = await demo.GetStringAsync($"{Listado}/{id}");
        var anterior = Regex.Match(detalle, "<option value=\"([0-9a-f-]{36})\">09:00 a 13:00").Groups[1].Value;
        var reemplazo = await FranjaAsync(demo, id, "10:00", "14:00", ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
            "2035-02-01", anterior);
        Assert.Equal(HttpStatusCode.Redirect, reemplazo.StatusCode);

        detalle = await demo.GetStringAsync($"{Listado}/{id}");
        Assert.Contains("31/01/2035", detalle);
        Assert.Contains("01/02/2035", detalle);
    }

    // Acordado con Lucas (18/10): mientras la reprogramación por lote esté pendiente, no se reemplaza una franja con
    // envíos comprometidos desde la fecha del cambio. Un envío con entrega el 10/03 bloquea un cambio desde el 01/03,
    // pero no uno desde el 11/03.
    [Fact]
    public async Task Una_franja_con_envios_comprometidos_desde_esa_fecha_no_se_reemplaza()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");
        var zonaId = await CrearAsync(demo, "T-BLOQUEO", "90040");
        var todosLosDias = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        await FranjaAsync(demo, zonaId, "09:00", "13:00", todosLosDias, "2035-01-01");
        var detalle = await demo.GetStringAsync($"{Listado}/{zonaId}");
        var franjaId = Guid.Parse(Regex.Match(detalle, "<option value=\"([0-9a-f-]{36})\">09:00 a 13:00").Groups[1].Value);
        await EnvioComprometidoAsync(zonaId, franjaId, new DateOnly(2035, 3, 10));

        var bloqueado = await TextoAsync(await FranjaAsync(demo, zonaId, "10:00", "14:00", todosLosDias, "2035-03-01", franjaId.ToString()));
        Assert.Contains("Hay envíos o rutas comprometidos con esta franja desde esa fecha.", bloqueado);

        var permitido = await FranjaAsync(demo, zonaId, "10:00", "14:00", todosLosDias, "2035-03-11", franjaId.ToString());
        Assert.Equal(HttpStatusCode.Redirect, permitido.StatusCode);
    }

    // Pedido de Lucas (19/10): un envío sin fecha propia, pero asignado a una ruta Planificada del 10/03, también
    // bloquea el cambio desde el 01/03; la franja anterior queda como estaba.
    [Fact]
    public async Task Un_envio_sin_fecha_en_una_ruta_desde_esa_fecha_tambien_bloquea_el_reemplazo()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");
        var (zonaId, franjaId) = await ZonaConFranjaAsync(demo, "T-RUTA", "90050");
        var envio = await EnvioComprometidoAsync(zonaId, franjaId, fecha: null, "90050");
        await EnRutaAsync(envio, new DateOnly(2035, 3, 10));

        var bloqueado = await TextoAsync(await FranjaAsync(demo, zonaId, "10:00", "14:00", TodosLosDias, "2035-03-01", franjaId.ToString()));

        Assert.Contains("Hay envíos o rutas comprometidos con esta franja desde esa fecha.", bloqueado);
        var detalle = await demo.GetStringAsync($"{Listado}/{zonaId}");
        Assert.DoesNotContain("28/02/2035", detalle);
        Assert.Contains($"<option value=\"{franjaId}\">09:00 a 13:00", detalle);
    }

    // Revisión de Ezequiel (19/10): si una regla falla, la página no muestra datos que no se guardaron. A2 al modificar
    // deja el nombre anterior; A3 al reemplazar deja la franja anterior abierta y elegible.
    [Fact]
    public async Task Si_una_regla_falla_la_pagina_muestra_lo_guardado()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");
        var (zonaId, franjaId) = await ZonaConFranjaAsync(demo, "T-VALIDA", "90060");
        await FranjaAsync(demo, zonaId, "14:00", "18:00", TodosLosDias, "2035-01-01");

        var modificar = await TextoAsync(await AccionAsync(demo, zonaId, "Modificar",
            [new("Nombre", "Nombre rechazado"), new("CodigosPostales", "90060, 11000")]));
        Assert.Contains("El código postal 11000 ya pertenece a la zona MVD-DEMO.", modificar);
        // El título muestra lo guardado; el campo del formulario conserva lo que escribió, para corregirlo.
        Assert.Contains("T-VALIDA · Zona T-VALIDA", modificar);
        Assert.DoesNotContain("T-VALIDA · Nombre rechazado", modificar);

        // 10 a 15 choca con la de la tarde (14 a 18).
        var reemplazo = await TextoAsync(await FranjaAsync(demo, zonaId, "10:00", "15:00", TodosLosDias, "2035-03-01", franjaId.ToString()));
        Assert.Contains("La franja se superpone con otra de la zona en el mismo día.", reemplazo);
        Assert.DoesNotContain("28/02/2035", reemplazo);
        // Sigue entre las que se pueden reemplazar (queda seleccionada, porque es la que eligió).
        Assert.Matches($"<option value=\"{franjaId}\"[^>]*>09:00 a 13:00", reemplazo);
    }

    // Revisión de Ezequiel (19/10): un cambio con fecha de hoy o pasada cerraría la franja hacia atrás; una franja nueva
    // no puede empezar en el pasado.
    [Fact]
    public async Task Un_cambio_o_una_franja_con_fecha_pasada_se_rechaza()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");
        var (zonaId, franjaId) = await ZonaConFranjaAsync(demo, "T-PASADO", "90070");

        var reemplazo = await TextoAsync(await FranjaAsync(demo, zonaId, "10:00", "14:00", TodosLosDias, "2020-01-01", franjaId.ToString()));
        Assert.Contains("El cambio de franja tiene que regir desde mañana o después.", reemplazo);

        var nueva = await TextoAsync(await FranjaAsync(demo, zonaId, "19:00", "21:00", TodosLosDias, "2020-01-01"));
        Assert.Contains("La franja tiene que regir desde hoy o después.", nueva);
    }

    // A1 sin distinguir mayúsculas: "t-may" y "T-MAY" son la misma zona.
    [Fact]
    public async Task El_codigo_no_distingue_mayusculas()
    {
        using var demo = await AdminAsync("admin@operador-demo.uy");
        await CrearAsync(demo, "t-may", "90080");

        var html = await TextoAsync(await EnviarAltaAsync(demo, "T-MAY", "90081"));

        Assert.Contains("Ya existe una zona con el código T-MAY.", html);
    }

    private static readonly string[] TodosLosDias = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    // Una zona con la franja de 9 a 13 todos los días desde el 01/01/2035, y el Id de esa franja.
    private static async Task<(Guid ZonaId, Guid FranjaId)> ZonaConFranjaAsync(HttpClient client, string codigo, string postal)
    {
        var zonaId = await CrearAsync(client, codigo, postal);
        await FranjaAsync(client, zonaId, "09:00", "13:00", TodosLosDias, "2035-01-01");
        var detalle = await client.GetStringAsync($"{Listado}/{zonaId}");
        return (zonaId, Guid.Parse(Regex.Match(detalle, "<option value=\"([0-9a-f-]{36})\">09:00 a 13:00").Groups[1].Value));
    }

    // Un envío de Demo con la franja comprometida, con fecha propia o sin ella. Se guarda con el operador Demo como
    // inquilino, igual que en ReprogramarEnvioTests. Sin fecha queda AsignadoARuta, como uno que ya está en una ruta.
    private async Task<Envio> EnvioComprometidoAsync(Guid zonaId, Guid franjaId, DateOnly? fecha, string postal = "90040")
    {
        var ahora = DateTimeOffset.UtcNow;
        var envio = Envio.Crear(DatosIniciales.OperadorDemoId, DatosIniciales.ComercioDemoId, $"ENV-Z-{Guid.NewGuid():N}",
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", postal),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, null, ahora);
        envio.DefinirPlanificacion(zonaId, franjaId, fecha);
        if (fecha is null)
        {
            envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora);
            envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora);
        }

        using var scope = factory.Services.CreateScope();
        await using var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider,
            new InquilinoFijo(DatosIniciales.OperadorDemoId, null));
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio;
    }

    // Una ruta Planificada de Demo para esa fecha, con el envío en una de sus paradas.
    private async Task EnRutaAsync(Envio envio, DateOnly fecha)
    {
        var ruta = Ruta.Crear(DatosIniciales.OperadorDemoId, fecha, Guid.NewGuid(), Guid.NewGuid(),
            [new EnvioRuta(envio.Id, envio.Numero, null, null, null, false, [])], DateTimeOffset.UtcNow);

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

    // Da de alta la zona y devuelve su Id, que viene en la redirección al detalle.
    private static async Task<Guid> CrearAsync(HttpClient client, string codigo, string postales)
    {
        var respuesta = await EnviarAltaAsync(client, codigo, postales);
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        return Guid.Parse(respuesta.Headers.Location!.ToString().Split('/').Last());
    }

    private static Task<HttpResponseMessage> EnviarAltaAsync(HttpClient client, string codigo, string postales)
    {
        return Sesion.EnviarFormularioAsync(client, $"{Listado}/nueva",
            new() { ["Codigo"] = codigo, ["Nombre"] = $"Zona {codigo}", ["CodigosPostales"] = postales });
    }

    // Los días se repiten con el mismo nombre de campo, como las casillas del formulario.
    private static Task<HttpResponseMessage> FranjaAsync(HttpClient client, Guid zonaId, string desde, string hasta,
        string[] dias, string vigenteDesde, string? reemplazaA = null)
    {
        var campos = new List<KeyValuePair<string, string>>
        {
            new("Desde", desde), new("Hasta", hasta), new("VigenteDesde", vigenteDesde), new("ReemplazaA", reemplazaA ?? ""),
        };
        campos.AddRange(dias.Select(d => new KeyValuePair<string, string>("Dias", d)));
        return AccionAsync(client, zonaId, "Franja", campos);
    }

    // Una acción del detalle (?handler=...), con el token antifalsificación de la página.
    private static async Task<HttpResponseMessage> AccionAsync(
        HttpClient client, Guid zonaId, string accion, List<KeyValuePair<string, string>> campos)
    {
        var pagina = $"{Listado}/{zonaId}";
        campos.Add(new("__RequestVerificationToken", await Sesion.TokenAsync(client, pagina)));
        return await client.PostAsync($"{pagina}?handler={accion}", new FormUrlEncodedContent(campos));
    }

    // Razor codifica las tildes: se decodifica para buscar el texto que ve el usuario.
    private static async Task<string> TextoAsync(HttpResponseMessage respuesta)
    {
        return WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
    }
}

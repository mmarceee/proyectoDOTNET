using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Logistica.Http.Contracts.Repartidor;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Logistica.Modules.Administracion.Infrastructure.Identity;

namespace Logistica.IntegrationTests.Ejecucion;

public class DescargarHojaDeRutaTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static int dia;
    private static readonly Guid Repartidor = Guid.Parse("41111111-1111-1111-1111-111111111111");
    private static readonly Guid Vehiculo = Guid.Parse("51111111-1111-1111-1111-111111111111");
    private static readonly Guid Zona = Guid.Parse("61111111-1111-1111-1111-111111111111");

    private HttpClient Cliente(DateTimeOffset ahora) => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Identity:PermitirTenantProvisorio", "false");
        b.ConfigureServices(s => s.AddSingleton<TimeProvider>(new Reloj(ahora)));
    }).CreateClient(Sesion.Opciones);

    private sealed class Reloj(DateTimeOffset ahora) : TimeProvider { public override DateTimeOffset GetUtcNow() => ahora; }

    // Cada escenario dispone de días contiguos propios para crear rutas anteriores y futuras.
    private static DateOnly Fecha() => new DateOnly(2040, 1, 1).AddDays(10 * Interlocked.Increment(ref dia));

    private async Task<Guid> PrepararAsync(DateOnly fecha, string estado = "Despachada", bool incompleta = false)
    {
        // Fuerza inicialización de la aplicación antes de preparar entidades.
        using var inicial = factory.CreateClient();
        await inicial.GetAsync("/health");
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("operador_id", DatosIniciales.OperadorDemoId.ToString())], "Prueba"))
        };
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var plan = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        var ahora = new DateTimeOffset(fecha.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var lista = new List<Envio>();
        for (var i = 0; i < 3; i++)
        {
            var envio = Envio.Crear(DatosIniciales.OperadorDemoId, DatosIniciales.ComercioDemoId, $"CU50-{Guid.NewGuid():N}",
                new Destinatario("Destinataria CU50", "099123456", "no-descargar@tienda.uy", "documento-privado"),
                new Direccion("Calle CU50", i < 2 ? "10" : "20", "Montevideo", "Montevideo", incompleta ? "99999" : "11300", "Portón azul"),
                [new DatosBulto(1, 10, 10, 10, 0), new DatosBulto(2, 10, 10, 10, 0)], OrigenEvento.PortalComercio, null, ahora);
            envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora);
            envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora);
            db.Envios.Add(envio); lista.Add(envio);
        }
        await db.SaveChangesAsync();
        var datos = lista.Select(e => new EnvioRuta(e.Id, e.Numero, fecha, Zona, null, false,
            e.Bultos.Select(b => new CargaBulto(e.Id, b.Id, b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm)).ToArray(),
            new PuntoEntrega(e.Direccion.Calle, e.Direccion.Numero, e.Direccion.Localidad, e.Direccion.Departamento, e.Direccion.CodigoPostal))).ToArray();
        var ruta = Ruta.Crear(DatosIniciales.OperadorDemoId, fecha, Repartidor, Vehiculo, datos, ahora);
        if (estado != "Planificada") ruta.Despachar(ahora);
        if (estado == "EnCurso") ruta.Iniciar();
        if (estado == "Finalizada") ruta.Finalizar();
        plan.Rutas.Add(ruta);
        await plan.SaveChangesAsync();
        return ruta.Id;
    }

    [Theory]
    [InlineData("Despachada", HttpStatusCode.OK)]
    [InlineData("EnCurso", HttpStatusCode.OK)]
    [InlineData("Planificada", HttpStatusCode.NoContent)]
    [InlineData("Finalizada", HttpStatusCode.NoContent)]
    public async Task Descarga_solo_ruta_del_dia_y_estados_habilitados(string estado, HttpStatusCode esperado)
    {
        var fecha = Fecha();
        var id = await PrepararAsync(fecha, estado);
        using var client = Cliente(new DateTimeOffset(fecha.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero));
        await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        var respuesta = await client.GetAsync("/api/repartidor/hoja-de-ruta");
        Assert.Equal(esperado, respuesta.StatusCode);
        Assert.True(respuesta.Headers.CacheControl?.NoStore);
        if (esperado != HttpStatusCode.OK) return;
        var hoja = (await respuesta.Content.ReadFromJsonAsync<HojaDeRutaResponse>())!;
        Assert.Equal(id, hoja.RutaId);
        Assert.Equal(fecha, hoja.FechaRuta);
        Assert.Equal(estado, hoja.EstadoRuta);
        Assert.Equal(2, hoja.Paradas.Count);
        Assert.Equal(3, hoja.Paradas.Sum(p => p.Envios.Count));
        Assert.Equal([1, 2], hoja.Paradas.Select(p => p.Orden));
        var envio = hoja.Paradas[0].Envios[0];
        Assert.Equal(2, envio.Bultos.Count);
        Assert.Equal("Portón azul", envio.Direccion.Referencia);
        Assert.True(envio.ReglasEntrega.FirmaObligatoria);
        // El envío de la prueba se crea sin versión de reglas: la hoja lleva la vigente del operador (CU-06).
        Assert.Equal("Vigente", envio.ReglasEntrega.Origen);
        Assert.NotNull(envio.ReglasEntrega.VersionId);
        Assert.NotEmpty(envio.ReglasEntrega.MotivosNoEntrega);
        Assert.Contains(envio.ReglasEntrega.MotivosNoEntrega, m => m.ExigeEvidencia);
        var json = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("no-descargar@", json);
        Assert.DoesNotContain("documento-privado", json);
        Assert.DoesNotContain("montoTarifa", json);
        Assert.All(hoja.Paradas.SelectMany(p => p.Envios), e => Assert.Equal("AsignadoARuta", e.Estado));
        var segunda = (await client.GetFromJsonAsync<HojaDeRutaResponse>("/api/repartidor/hoja-de-ruta"))!;
        Assert.Equal(hoja.RevisionRuta, segunda.RevisionRuta);
    }

    [Fact]
    public async Task Una_ruta_anterior_abierta_se_descarga_por_id_sin_cambiar_la_descarga_del_dia()
    {
        var fecha = Fecha();
        var id = await PrepararAsync(fecha, "EnCurso");
        // Día sin otras rutas de esta fixture compartida.
        using var client = Cliente(new DateTimeOffset(fecha.AddYears(1).ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero));
        await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/repartidor/hoja-de-ruta")).StatusCode);
        var respuesta = await client.GetAsync($"/api/repartidor/rutas/{id}/hoja-de-ruta");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.True(respuesta.Headers.CacheControl?.NoStore);
        var hoja = (await respuesta.Content.ReadFromJsonAsync<HojaDeRutaResponse>())!;
        Assert.Equal(id, hoja.RutaId);
        Assert.Equal(fecha, hoja.FechaRuta);
        Assert.Equal(fecha.AddYears(1), hoja.FechaOperativa);
    }

    [Theory]
    [InlineData("Planificada", 0)]
    [InlineData("Finalizada", 0)]
    [InlineData("Despachada", -1)]
    public async Task Elegir_un_id_no_habilita_rutas_cerradas_planificadas_o_futuras(string estado, int desplazamiento)
    {
        var fecha = Fecha();
        var id = await PrepararAsync(fecha, estado);
        using var client = Cliente(new DateTimeOffset(fecha.AddDays(desplazamiento).ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero));
        await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync($"/api/repartidor/rutas/{id}/hoja-de-ruta")).StatusCode);
    }

    [Fact]
    public async Task Mis_rutas_incluye_pendientes_anteriores_y_estado_actual_sin_depender_del_aviso()
    {
        var fecha = Fecha();
        var abierta = await PrepararAsync(fecha, "EnCurso");
        var cerrada = await PrepararAsync(fecha.AddDays(-1), "Finalizada");
        var planificada = await PrepararAsync(fecha.AddDays(1), "Planificada");
        using var client = Cliente(new DateTimeOffset(fecha.AddDays(2).ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero));
        await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        var todas = new List<TarjetaRutaResponse>();
        for (var pagina = 1; ; pagina++)
        {
            var respuesta = await client.GetAsync($"/api/repartidor/rutas?pagina={pagina}");
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.True(respuesta.Headers.CacheControl?.NoStore);
            var listado = (await respuesta.Content.ReadFromJsonAsync<MisRutasResponse>())!;
            Assert.Equal(fecha.AddDays(2), listado.FechaOperativa);
            todas.AddRange(listado.Rutas);
            if (listado.Rutas.Count < 20) break;
        }
        var tarjeta = Assert.Single(todas, r => r.RutaId == abierta);
        Assert.Equal("EnCurso", tarjeta.Estado);
        Assert.Equal(fecha, tarjeta.FechaRuta);
        Assert.False(string.IsNullOrWhiteSpace(tarjeta.Matricula));
        Assert.Equal(3, tarjeta.Envios);
        Assert.Equal(2, tarjeta.Paradas);
        Assert.True(tarjeta.CargaPendiente);
        Assert.False(tarjeta.RendicionIniciada);
        Assert.Equal("Finalizada", Assert.Single(todas, r => r.RutaId == cerrada).Estado);
        Assert.DoesNotContain(todas, r => r.RutaId == planificada);
        Assert.True(todas.FindIndex(r => r.RutaId == abierta) < todas.FindIndex(r => r.RutaId == cerrada));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/repartidor/rutas?pagina=0")).StatusCode);
        using var otro = Cliente(new DateTimeOffset(fecha.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero));
        await Sesion.IngresarAsync(otro, "repartidor@envios-del-este.uy");
        Assert.Equal(HttpStatusCode.NoContent, (await otro.GetAsync($"/api/repartidor/rutas/{abierta}/hoja-de-ruta")).StatusCode);
        var ajenas = (await otro.GetFromJsonAsync<MisRutasResponse>("/api/repartidor/rutas"))!;
        Assert.DoesNotContain(ajenas.Rutas, r => r.RutaId == abierta || r.RutaId == cerrada);
    }

    [Fact]
    public async Task Dia_operativo_usa_zona_del_operador_y_no_UTC()
    {
        var fecha = Fecha();
        var id = await PrepararAsync(fecha);
        using var client = Cliente(new DateTimeOffset(fecha.AddDays(1).ToDateTime(new TimeOnly(1, 30)), TimeSpan.Zero));
        await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        var sesion = (await client.GetFromJsonAsync<SesionRepartidorResponse>("/api/repartidor/sesion"))!;
        Assert.Equal(fecha, sesion.FechaOperativa);
        Assert.Equal("America/Montevideo", sesion.ZonaHorariaOperador);
        Assert.Equal(id, (await client.GetFromJsonAsync<HojaDeRutaResponse>("/api/repartidor/hoja-de-ruta"))!.RutaId);
    }

    [Fact]
    public async Task Otro_operador_no_ve_la_ruta_y_no_se_admite_elegir_repartidor_por_query()
    {
        var fecha = Fecha();
        await PrepararAsync(fecha);
        using var este = Cliente(new DateTimeOffset(fecha.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero));
        await Sesion.IngresarAsync(este, "repartidor@envios-del-este.uy");
        Assert.Equal(HttpStatusCode.NoContent, (await este.GetAsync($"/api/repartidor/hoja-de-ruta?repartidorId={Repartidor}&operadorId={DatosIniciales.OperadorDemoId}")).StatusCode);
        using var otroDia = Cliente(new DateTimeOffset(fecha.AddDays(1).ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero));
        await Sesion.IngresarAsync(otroDia, "repartidor@operador-demo.uy");
        Assert.Equal(HttpStatusCode.NoContent, (await otroDia.GetAsync($"/api/repartidor/hoja-de-ruta?fecha={fecha:yyyy-MM-dd}")).StatusCode);
    }

    [Fact]
    public async Task Una_hoja_incompleta_no_se_descarga_parcialmente()
    {
        var fecha = Fecha();
        await PrepararAsync(fecha, incompleta: true);
        using var client = Cliente(new DateTimeOffset(fecha.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero));
        await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/repartidor/hoja-de-ruta")).StatusCode);
    }

    [Theory]
    [InlineData("admin@operador-demo.uy")]
    [InlineData("despacho@operador-demo.uy")]
    [InlineData("deposito@operador-demo.uy")]
    [InlineData("comercio@tiendademo.uy")]
    public async Task Otros_roles_no_descargan(string email)
    {
        using var client = Cliente(DateTimeOffset.UtcNow);
        await Sesion.IngresarAsync(client, email);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/repartidor/hoja-de-ruta")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/repartidor/sesion")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/repartidor/rutas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/repartidor/rutas/{Guid.NewGuid()}/hoja-de-ruta")).StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_no_hay_descarga()
    {
        using var client = Cliente(DateTimeOffset.UtcNow);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/repartidor/hoja-de-ruta")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/repartidor/sesion")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/repartidor/rutas")).StatusCode);
    }

    [Fact]
    public async Task Otro_repartidor_del_mismo_operador_no_ve_la_ruta()
    {
        var fecha = Fecha();
        var rutaAjena = await PrepararAsync(fecha);
        using (var scope = factory.Services.CreateScope())
        {
            var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            var usuario = Usuario.CrearPersonal(DatosIniciales.OperadorDemoId, "Segundo repartidor CU50", "repartidor@segundo-demo.uy");
            Assert.True((await usuarios.CreateAsync(usuario, UsuariosIniciales.ContrasenaDemo)).Succeeded);
            Assert.True((await usuarios.AddToRoleAsync(usuario, "Repartidor")).Succeeded);
            var admin = scope.ServiceProvider.GetRequiredService<AdministracionDbContext>();
            var segundo = await admin.Repartidores.SingleAsync(r => r.Id == Guid.Parse("42222222-2222-2222-2222-222222222222"));
            segundo.VincularUsuario(usuario.Id);
            await admin.SaveChangesAsync();
        }
        using var client = Cliente(new DateTimeOffset(fecha.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero));
        await Sesion.IngresarAsync(client, "repartidor@segundo-demo.uy");
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync($"/api/repartidor/hoja-de-ruta?repartidorId={Repartidor}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync($"/api/repartidor/rutas/{rutaAjena}/hoja-de-ruta")).StatusCode);
        Assert.DoesNotContain((await client.GetFromJsonAsync<MisRutasResponse>("/api/repartidor/rutas"))!.Rutas, r => r.RutaId == rutaAjena);
    }

    [Fact]
    public async Task Un_vinculo_deshabilitado_se_rechaza_aunque_la_cookie_ya_exista()
    {
        var fecha = Fecha();
        await PrepararAsync(fecha);
        using var client = Cliente(new DateTimeOffset(fecha.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero));
        await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        using var scope = factory.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<AdministracionDbContext>();
        var repartidor = await admin.Repartidores.SingleAsync(r => r.Id == Repartidor);
        admin.Entry(repartidor).Property(r => r.Activo).CurrentValue = false;
        await admin.SaveChangesAsync();
        try
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/repartidor/sesion")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/repartidor/hoja-de-ruta")).StatusCode);
        }
        finally
        {
            admin.Entry(repartidor).Property(r => r.Activo).CurrentValue = true;
            await admin.SaveChangesAsync();
        }
    }
}

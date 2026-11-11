using System.Net;
using System.Net.Http.Json;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Planificacion;
using Logistica.Http.Contracts.Repartidor;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Reglas;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Planificacion;

// Escenarios RF34 contra PostgreSQL real. La preparación acredita explícitamente la recepción física;
// las decisiones de ruta, carga, visita y conciliación se ejercen por HTTP.
public sealed class Rf34BultosTests : IClassFixture<PostgresApiFactory>, IDisposable
{
    private static int dia;
    private static readonly Guid Operador = DatosIniciales.OperadorDemoId;
    private static readonly Guid Repartidor = DatosIniciales.RepartidorDemoId;
    private static readonly Guid Vehiculo = Guid.Parse("51111111-1111-1111-1111-111111111111");
    private static readonly Guid OtroRepartidor = Guid.Parse("42222222-2222-2222-2222-222222222222");
    private static readonly Guid OtroVehiculo = Guid.Parse("52222222-2222-2222-2222-222222222222");
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jD8kAAAAASUVORK5CYII=";
    private readonly WebApplicationFactory<Program> host;
    private readonly HttpClient despacho;
    private readonly DateTimeOffset ahora;
    private DateOnly Fecha => DateOnly.FromDateTime(ahora.UtcDateTime);

    public Rf34BultosTests(PostgresApiFactory factory)
    {
        ahora = new DateTimeOffset(2035, 5, 1, 15, 0, 0, TimeSpan.Zero).AddDays(Interlocked.Increment(ref dia));
        host = factory.WithWebHostBuilder(b => b.UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true")
            .ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new Reloj(ahora))));
        despacho = host.CreateClient(Sesion.Opciones);
    }

    [Fact]
    public async Task Dos_despachadores_compiten_por_el_mismo_bulto_y_solo_uno_reserva()
    {
        var e = await PrepararAsync();
        var peticion = Pedido(e, [e.Bultos[0]]);
        using var otro = host.CreateClient(Sesion.Opciones);
        var respuestas = await Task.WhenAll(despacho.PostAsJsonAsync("/api/planificacion/rutas", peticion),
            otro.PostAsJsonAsync("/api/planificacion/rutas", peticion with { RepartidorId = OtroRepartidor,
                VehiculoId = OtroVehiculo, OperacionId = Guid.NewGuid() }));
        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Conflict);
        using var scope = host.Services.CreateScope();
        await using var plan = Contexto<PlanificacionDbContext>(scope);
        Assert.Single(await plan.ReservasBulto.Where(r => r.BultoId == e.Bultos[0].Id && r.CerradaEn == null).ToListAsync());
    }

    [Fact]
    public async Task Subconjuntos_del_mismo_envio_van_a_rutas_distintas_y_capacidad_cuenta_solo_su_manifiesto()
    {
        var e = await PrepararAsync();
        var a = await CrearAsync(Pedido(e, [e.Bultos[0]]));
        var b = await CrearAsync(Pedido(e, [e.Bultos[1]]) with { RepartidorId = OtroRepartidor, VehiculoId = OtroVehiculo });
        Assert.NotEqual(a.Id, b.Id);
        using var scope = host.Services.CreateScope();
        await using var plan = Contexto<PlanificacionDbContext>(scope);
        var reservas = await plan.ReservasBulto.Where(r => r.EnvioId == e.Id).ToListAsync();
        Assert.Equal(2, reservas.Count);
        Assert.Equal(2, reservas.Select(r => r.BultoId).Distinct().Count());
        var validacion = await plan.Validaciones.Include(v => v.Bultos).SingleAsync(v => v.RutaId == a.Id);
        Assert.Equal(e.Bultos[0].Id, Assert.Single(validacion.Bultos).BultoId);
        Assert.Equal(1m, validacion.Bultos.Sum(x => x.PesoKg));
        // Última defensa de PostgreSQL, incluso si un escritor omite el bloqueo de aplicación.
        var ruta = (await plan.Rutas.Include(r => r.Paradas).ThenInclude(p => p.Envios).SingleAsync(r => r.Id == b.Id));
        plan.ReservasBulto.Add(ReservaBulto.Abrir(ruta, e.Id, e.Bultos[0].Id, Guid.NewGuid(), null, ahora));
        await Assert.ThrowsAsync<DbUpdateException>(() => plan.SaveChangesAsync());
    }

    [Fact]
    public async Task Reenviar_creacion_devuelve_el_resultado_original_y_reutilizar_identidad_con_otro_contenido_falla()
    {
        var e = await PrepararAsync();
        var pedido = Pedido(e, [e.Bultos[0]]);
        var a = await CrearAsync(pedido);
        var b = await CrearAsync(pedido);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(a.Revision, b.Revision);
        var cambio = await despacho.PostAsJsonAsync("/api/planificacion/rutas", pedido with { BultoIds = [e.Bultos[1].Id] });
        Assert.Equal(HttpStatusCode.BadRequest, cambio.StatusCode);
        using var scope = host.Services.CreateScope();
        await using var plan = Contexto<PlanificacionDbContext>(scope);
        Assert.Single(await plan.ReservasBulto.Where(r => r.EnvioId == e.Id).ToListAsync());
    }

    [Fact]
    public async Task Agregar_otro_bulto_del_mismo_envio_revalida_capacidad_y_reenviar_no_duplica_reservas()
    {
        var e = await PrepararAsync();
        var ruta = await CrearAsync(Pedido(e, [e.Bultos[0]]));
        var pedido = new AgregarEnviosRutaRequest(ruta.Revision, [e.Id], [e.Bultos[1].Id], Guid.NewGuid(),
            new Dictionary<Guid, long> { [e.Bultos[1].Id] = e.Bultos[1].RevisionCustodia });
        var r = await despacho.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/envios", pedido);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        var actualizado = (await r.Content.ReadFromJsonAsync<RutaResponse>())!;
        Assert.Equal(ruta.Revision + 1, actualizado.Revision);
        Assert.Equal(HttpStatusCode.OK, (await despacho.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/envios", pedido)).StatusCode);
        using var scope = host.Services.CreateScope(); await using var plan = Contexto<PlanificacionDbContext>(scope);
        Assert.Equal(2, await plan.ReservasBulto.CountAsync(x => x.RutaId == ruta.Id && x.CerradaEn == null));
        var validacion = await plan.Validaciones.Include(v => v.Bultos).SingleAsync(v => v.RutaId == ruta.Id && v.RevisionRuta == actualizado.Revision);
        Assert.Equal(2m, validacion.Bultos.Sum(b => b.PesoKg));
    }

    [Fact]
    public async Task Revision_de_custodia_obsoleta_no_reserva_aunque_el_bulto_siga_en_deposito()
    {
        var e = await PrepararAsync();
        using var deposito = await DepositoAsync();
        var fila = (await Custodias(deposito, e.Id))[0];
        Assert.Equal(HttpStatusCode.OK, (await ConciliarAsync(deposito, Conciliar(fila, true))).StatusCode);
        var original = e.Bultos.Single(b => b.Id == fila.BultoId);
        Assert.Equal(HttpStatusCode.Conflict, (await despacho.PostAsJsonAsync("/api/planificacion/rutas", Pedido(e, [original]))).StatusCode);
        using var scope = host.Services.CreateScope(); await using var plan = Contexto<PlanificacionDbContext>(scope);
        Assert.False(await plan.ReservasBulto.AnyAsync(x => x.EnvioId == e.Id));
    }

    [Fact]
    public async Task Quitar_es_idempotente_y_reagregar_conserva_historial_sin_duplicar_el_manifiesto()
    {
        var e = await PrepararAsync();
        var ruta = await CrearAsync(Pedido(e, [e.Bultos[0]]));
        var pedido = new QuitarEnvioDeRutaRequest(ruta.Revision, Guid.NewGuid());
        RutaResponse? quitada = null;
        for (var i = 0; i < 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/planificacion/rutas/{ruta.Id}/envios/{e.Id}")
            { Content = JsonContent.Create(pedido) };
            var respuesta = await despacho.SendAsync(request);
            Assert.True(respuesta.IsSuccessStatusCode, await respuesta.Content.ReadAsStringAsync());
            var actual = (await respuesta.Content.ReadFromJsonAsync<RutaResponse>())!;
            if (quitada is not null) Assert.Equal(quitada.Revision, actual.Revision);
            quitada = actual;
        }
        var agregar = await despacho.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/envios", new AgregarEnviosRutaRequest(
            quitada!.Revision, [e.Id], [e.Bultos[0].Id], Guid.NewGuid(), new Dictionary<Guid, long> { [e.Bultos[0].Id] = 1 }));
        Assert.True(agregar.IsSuccessStatusCode, await agregar.Content.ReadAsStringAsync());
        var nuevamente = await DespacharAsync((await agregar.Content.ReadFromJsonAsync<RutaResponse>())!);
        using var repartidor = await RepartidorAsync();
        var hoja = (await repartidor.GetFromJsonAsync<HojaDeRutaResponse>($"/api/repartidor/rutas/{ruta.Id}/hoja-de-ruta"))!;
        Assert.Equal(e.Bultos[0].Id, Assert.Single(Assert.Single(Assert.Single(hoja.Paradas).Envios).Bultos).Id);
        using var scope = host.Services.CreateScope(); await using var plan = Contexto<PlanificacionDbContext>(scope);
        var historial = await plan.ReservasBulto.Where(r => r.RutaId == nuevamente.Id && r.BultoId == e.Bultos[0].Id).ToListAsync();
        Assert.Equal(2, historial.Count);
        Assert.Single(historial, r => r.CerradaEn == null);
        Assert.Single(historial, r => r.CausaCierre == "QuitadoDeRutaPlanificada");
    }

    [Fact]
    public async Task Desmarcar_todos_los_bultos_en_la_pantalla_no_asigna_el_envio_entero()
    {
        var e = await PrepararAsync();
        var token = await Sesion.TokenAsync(despacho, "/backoffice/rutas/nueva");
        var respuesta = await despacho.PostAsync("/backoffice/rutas/nueva?handler=Confirmar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["Fecha"] = Fecha.ToString("yyyy-MM-dd"),
            ["RepartidorId"] = Repartidor.ToString(), ["VehiculoId"] = Vehiculo.ToString(), ["Seleccion"] = e.Id.ToString(),
            ["SeleccionPorBulto"] = "true", ["OperacionId"] = Guid.NewGuid().ToString(),
        }));
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("Seleccioná bultos válidos", WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()));
        using var scope = host.Services.CreateScope(); await using var plan = Contexto<PlanificacionDbContext>(scope);
        Assert.False(await plan.ReservasBulto.AnyAsync(x => x.EnvioId == e.Id));
    }

    [Fact]
    public async Task Faltante_confirmado_se_reasigna_mientras_los_cargados_conservan_custodia_y_visita_propias()
    {
        var e = await PrepararAsync();
        var ruta = await CrearAsync(Pedido(e, e.Bultos));
        ruta = await DespacharAsync(ruta);
        using var repartidor = await RepartidorAsync();
        var carga = Carga(ruta, e.Bultos.Take(2).ToArray(), e.Bultos.Skip(2).ToArray());
        Assert.Equal("Aplicada", (await Enviar(repartidor, carga)).Resultado);
        Assert.Equal("YaAplicada", (await Enviar(repartidor, carga)).Resultado);
        using var deposito = await DepositoAsync();
        var filas = await Custodias(deposito, e.Id);
        Assert.All(filas.Take(2).Where(b => b.Ubicacion == "Repartidor"), b => Assert.Equal(ruta.Id, b.RutaId));
        var tercero = Assert.Single(filas, b => b.BultoId == e.Bultos[2].Id);
        var pedido = Conciliar(tercero, true);
        var ok = await ConciliarAsync(deposito, pedido);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConciliarAsync(deposito, pedido)).StatusCode);
        var liberado = Assert.Single(await Custodias(deposito, e.Id), b => b.BultoId == tercero.BultoId);
        Assert.Null(liberado.RutaId);
        var otra = await CrearAsync(Pedido(e, [e.Bultos[2]]) with { RepartidorId = OtroRepartidor,
            VehiculoId = OtroVehiculo, RevisionesBultos = new Dictionary<Guid, long> { [liberado.BultoId] = liberado.Revision } });
        Assert.NotEqual(ruta.Id, otra.Id);
        Assert.Equal("Aplicada", (await Enviar(repartidor, new(Guid.NewGuid(), carga.DispositivoId, 2, "IniciarRuta",
            ruta.Id, null, ahora, null, null))).Resultado);
        var hoja = await repartidor.GetFromJsonAsync<HojaDeRutaResponse>($"/api/repartidor/rutas/{ruta.Id}/hoja-de-ruta");
        var bultos = Assert.Single(Assert.Single(hoja!.Paradas).Envios).Bultos;
        Assert.Equal("EnDeposito", bultos.Single(b => b.Id == tercero.BultoId).Estado);
        var archivo = new ArchivoRequest("image/png", Png);
        var visita = new OperacionDispositivoRequest(Guid.NewGuid(), carga.DispositivoId, 3, "RegistrarVisita", ruta.Id,
            e.Id, ahora, -34.9m, -56.1m, Visita: new(e.Bultos.Take(2).Select(b => new ResultadoBultoRequest(b.Codigo)).ToArray(), archivo, archivo));
        Assert.Equal("Aplicada", (await Enviar(repartidor, visita)).Resultado);
        Assert.Equal("YaAplicada", (await Enviar(repartidor, visita)).Resultado);
        var estado = await LeerEnvio(e.Id);
        Assert.Equal(2, estado.BultosEntregados);
        Assert.Equal(EstadoEnvio.AsignadoARuta, estado.Bultos.Single(b => b.Id == tercero.BultoId).Estado);
        Assert.Equal(0, estado.Bultos.Single(b => b.Id == tercero.BultoId).IntentosFallidos);
        using var scope = host.Services.CreateScope();
        await using var plan = Contexto<PlanificacionDbContext>(scope);
        Assert.Equal(2, await plan.ReservasBulto.CountAsync(r => r.RutaId == ruta.Id && r.CausaCierre == "Entregado"));
    }

    [Fact]
    public async Task Reprogramado_en_vehiculo_sigue_bloqueado_tras_cierre_y_retorno_no_borra_intento()
    {
        var e = await PrepararAsync();
        var ruta = await DespacharAsync(await CrearAsync(Pedido(e, e.Bultos)));
        using var repartidor = await RepartidorAsync();
        var carga = Carga(ruta, e.Bultos, []);
        Assert.Equal("Aplicada", (await Enviar(repartidor, carga)).Resultado);
        Assert.Equal("Aplicada", (await Enviar(repartidor, new(Guid.NewGuid(), carga.DispositivoId, 2, "IniciarRuta", ruta.Id, null, ahora, null, null))).Resultado);
        Guid ausente;
        using (var scope = host.Services.CreateScope())
        {
            await using var admin = Contexto<AdministracionDbContext>(scope);
            ausente = await admin.MotivosNoEntrega.Where(m => m.VersionReglasId == e.VersionReglasId && m.Codigo == "AUSENTE").Select(m => m.Id).SingleAsync();
        }
        var archivo = new ArchivoRequest("image/png", Png);
        var visita = new OperacionDispositivoRequest(Guid.NewGuid(), carga.DispositivoId, 3, "RegistrarVisita", ruta.Id,
            e.Id, ahora, -34.9m, -56.1m, Visita: new(e.Bultos.Select((b, i) => new ResultadoBultoRequest(b.Codigo, i == 2 ? ausente : null)).ToArray(), archivo, archivo));
        Assert.Equal("Aplicada", (await Enviar(repartidor, visita)).Resultado);
        await CerrarAdministrativamente(ruta.Id);
        var noDisponible = await despacho.PostAsJsonAsync("/api/planificacion/rutas", Pedido(e, [e.Bultos[2]]) with { Fecha = Fecha.AddDays(2) });
        Assert.Equal(HttpStatusCode.Conflict, noDisponible.StatusCode);
        using var deposito = await DepositoAsync();
        var pendiente = Assert.Single(await Custodias(deposito, e.Id), b => b.BultoId == e.Bultos[2].Id);
        Assert.Equal("Repartidor", pendiente.Ubicacion);
        Assert.Equal(HttpStatusCode.OK, (await ConciliarAsync(deposito, Conciliar(pendiente, true))).StatusCode);
        var vuelto = Assert.Single(await Custodias(deposito, e.Id), b => b.BultoId == pendiente.BultoId);
        Assert.Equal("Reprogramado", vuelto.Estado);
        Assert.Null(vuelto.RutaId);
        var nueva = await CrearAsync(Pedido(e, [e.Bultos[2]]) with { Fecha = Fecha.AddDays(2),
            RevisionesBultos = new Dictionary<Guid, long> { [vuelto.BultoId] = vuelto.Revision } });
        Assert.NotEqual(ruta.Id, nueva.Id);
        var final = await LeerEnvio(e.Id);
        Assert.Equal(1, final.Bultos.Single(b => b.Id == pendiente.BultoId).IntentosFallidos);
        Assert.Equal(2, final.BultosEntregados);
    }

    [Fact]
    public async Task Conciliacion_es_atomica_rechaza_revision_obsoleta_y_no_localizado_conserva_reserva()
    {
        var e = await PrepararAsync();
        await CrearAsync(Pedido(e, e.Bultos));
        using var deposito = await DepositoAsync();
        var filas = await Custodias(deposito, e.Id);
        var pedido = Conciliar(filas[0], true) with { Bultos = [new(filas[0].BultoId, filas[0].Revision), new(filas[1].BultoId, filas[1].Revision + 1)] };
        Assert.Equal(HttpStatusCode.Conflict, (await ConciliarAsync(deposito, pedido)).StatusCode);
        var sinCambiar = await Custodias(deposito, e.Id);
        Assert.All(sinCambiar, b => Assert.Equal(1, b.Revision));
        Assert.All(sinCambiar, b => Assert.NotNull(b.RutaId));
        var desconocido = Conciliar(filas[0], false);
        Assert.Equal(HttpStatusCode.OK, (await ConciliarAsync(deposito, desconocido)).StatusCode);
        var bloqueado = Assert.Single(await Custodias(deposito, e.Id), b => b.BultoId == filas[0].BultoId);
        Assert.Equal("Desconocida", bloqueado.Ubicacion);
        Assert.NotNull(bloqueado.RutaId);
        Assert.Equal(HttpStatusCode.Conflict, (await despacho.PostAsJsonAsync("/api/planificacion/rutas", Pedido(e, [e.Bultos[0]])
            with { RepartidorId = OtroRepartidor, VehiculoId = OtroVehiculo })).StatusCode);
    }

    [Fact]
    public async Task Custodia_exige_operario_antiforgery_y_aislamiento_por_operador()
    {
        var e = await PrepararAsync();
        using var deposito = await DepositoAsync();
        var fila = (await Custodias(deposito, e.Id))[0];
        Assert.Equal(HttpStatusCode.BadRequest, (await deposito.PostAsJsonAsync("/api/deposito/custodia/conciliaciones", Conciliar(fila, true))).StatusCode);
        using var otro = host.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(otro, "deposito@envios-del-este.uy");
        Assert.Equal(HttpStatusCode.NotFound, (await otro.GetAsync($"/api/deposito/custodia/{e.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ConciliarAsync(otro, Conciliar(fila, true))).StatusCode);
        using var repartidor = await RepartidorAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await repartidor.GetAsync($"/api/deposito/custodia/{e.Id}")).StatusCode);
        var html = await deposito.GetStringAsync($"/backoffice/custodia?CodigoBulto={e.Bultos[0].Codigo}");
        Assert.Contains("Confirmar los bultos seleccionados", html);
        Assert.Contains(e.Bultos[0].Codigo, html);
    }

    private async Task<Envio> PrepararAsync()
    {
        using var scope = host.Services.CreateScope();
        // Cada escenario usa envíos propios; libera sólo recursos administrativos de escenarios anteriores.
        await using (var plan = Contexto<PlanificacionDbContext>(scope))
        {
            foreach (var ruta in await plan.Rutas.Include(r => r.Paradas).ThenInclude(p => p.Envios)
                .Where(r => r.Estado == EstadoRuta.EnCurso || r.Estado == EstadoRuta.Despachada).ToListAsync()) ruta.Finalizar();
            await plan.SaveChangesAsync();
        }
        await using var admin = Contexto<AdministracionDbContext>(scope);
        var anterior = await admin.VersionesReglas.Include(v => v.Motivos).OrderByDescending(v => v.Numero).FirstAsync();
        var valores = anterior.Valores() with { EntregaParcial = new ReglasEntregaParcial(true, PoliticaPendientes.NuevaVisitaConRecargo, 150) };
        var version = VersionReglas.CrearBorrador(Operador, anterior.Numero + 1, valores, anterior.Motivos.Select(m => m.Datos()));
        version.Publicar(ahora, ahora, anterior);
        admin.VersionesReglas.Add(version); await admin.SaveChangesAsync();
        var e = Envio.Crear(Operador, DatosIniciales.ComercioDemoId, $"RF34-{Guid.NewGuid():N}".ToUpperInvariant(), new Destinatario("Ana", "099123456"),
            new Direccion("Calle", "1", "Montevideo", "Montevideo", "11300"),
            Enumerable.Repeat(new DatosBulto(1, 10, 10, 10, 100), 3).ToArray(), OrigenEvento.PortalComercio, null, ahora, versionReglasId: version.Id);
        foreach (var b in e.Bultos)
        {
            e.RecibirBulto(b.Id, null, ahora);
            b.ConfirmarCustodia(UbicacionCustodia.Deposito, null, Guid.NewGuid(), Guid.NewGuid(), ahora, "Recepción física acreditada en la preparación");
        }
        await using var db = Contexto<EnviosDbContext>(scope);
        db.Envios.Add(e); await db.SaveChangesAsync();
        return e;
    }

    private CrearRutaRequest Pedido(Envio e, IReadOnlyList<Bulto> bultos) => new(Fecha, Repartidor, Vehiculo, [e.Id],
        bultos.Select(b => b.Id).ToArray(), Guid.NewGuid(), bultos.ToDictionary(b => b.Id, b => b.RevisionCustodia));
    private async Task<RutaResponse> CrearAsync(CrearRutaRequest pedido)
    {
        var r = await despacho.PostAsJsonAsync("/api/planificacion/rutas", pedido);
        Assert.True(r.StatusCode == HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<RutaResponse>())!;
    }
    private async Task<RutaResponse> DespacharAsync(RutaResponse ruta)
    {
        var r = await despacho.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/despacho", new DespacharRutaRequest(ruta.Revision));
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<RutaResponse>())!;
    }
    private OperacionDispositivoRequest Carga(RutaResponse ruta, IReadOnlyList<Bulto> bultos, IReadOnlyList<Bulto> faltantes)
        => new(Guid.NewGuid(), Guid.NewGuid(), 1, "ConfirmarCarga", ruta.Id, null, ahora, null, null,
            Carga: new(ruta.Revision, bultos.Select(b => new LecturaCargaRequest(b.Codigo, ahora)).ToArray(), faltantes.Count > 0,
                PorBulto: true, Faltantes: faltantes.Select(b => new FaltanteCargaRequest(b.Codigo, "NoLocalizado")).ToArray()));
    private static ConciliarBultosRequest Conciliar(CustodiaBultoResponse fila, bool enDeposito) => new(Guid.NewGuid(), fila.RutaId,
        fila.RevisionRuta, [new(fila.BultoId, fila.Revision)], enDeposito, "Verificación física de depósito RF34");
    private async Task<HttpResponseMessage> ConciliarAsync(HttpClient client, ConciliarBultosRequest pedido)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/deposito/custodia/conciliaciones") { Content = JsonContent.Create(pedido) };
        request.Headers.Add("RequestVerificationToken", await Sesion.TokenAsync(client, "/backoffice/custodia"));
        return await client.SendAsync(request);
    }
    private static async Task<CustodiaBultoResponse[]> Custodias(HttpClient c, Guid envio)
        => (await c.GetFromJsonAsync<CustodiaBultoResponse[]>($"/api/deposito/custodia/{envio}"))!;
    private async Task<HttpClient> DepositoAsync()
    {
        var c = host.CreateClient(Sesion.Opciones); await Sesion.IngresarAsync(c, "deposito@operador-demo.uy"); return c;
    }
    private async Task<HttpClient> RepartidorAsync()
    {
        var c = host.CreateClient(Sesion.Opciones); await Sesion.IngresarAsync(c, "repartidor@operador-demo.uy"); return c;
    }
    private static async Task<ResultadoOperacionResponse> Enviar(HttpClient cliente, OperacionDispositivoRequest op)
    {
        var r = await cliente.PostAsJsonAsync("/api/repartidor/sincronizacion", new SincronizacionRequest([op]));
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return Assert.Single((await r.Content.ReadFromJsonAsync<SincronizacionResponse>())!.Resultados);
    }
    private async Task<Envio> LeerEnvio(Guid id)
    {
        using var scope = host.Services.CreateScope(); await using var db = Contexto<EnviosDbContext>(scope);
        return await db.Envios.Include(e => e.Intentos).SingleAsync(e => e.Id == id);
    }
    private async Task CerrarAdministrativamente(Guid id)
    {
        using var scope = host.Services.CreateScope(); await using var plan = Contexto<PlanificacionDbContext>(scope);
        var ruta = await plan.Rutas.Include(r => r.Paradas).ThenInclude(p => p.Envios).SingleAsync(r => r.Id == id);
        ruta.Finalizar(); await plan.SaveChangesAsync();
    }
    private static T Contexto<T>(IServiceScope scope) where T : Logistica.BuildingBlocks.Infrastructure.Persistence.ModuleDbContext
        => ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider, new InquilinoFijo(Operador, null));
    public void Dispose() { despacho.Dispose(); host.Dispose(); }
    private sealed class Reloj(DateTimeOffset ahora) : TimeProvider { public override DateTimeOffset GetUtcNow() => ahora; }
}

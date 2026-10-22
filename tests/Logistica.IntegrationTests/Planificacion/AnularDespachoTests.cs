using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Logistica.Http.Contracts.Planificacion;
using Logistica.Http.Contracts.Repartidor;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Administracion.Infrastructure.Identity;
using Logistica.Modules.Administracion.Domain.Operadores;
using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Logistica.Modules.Ejecucion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Application.Abstractions;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Planificacion;

public sealed class AnularDespachoTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static int dia;
    private sealed record Datos(Guid Ruta, long Revision, DateOnly Fecha, DateTimeOffset Despacho, Guid Envio,
        string Codigo, Guid Repartidor, Guid Vehiculo);
    private HttpClient Cliente(bool desarrollo = true) => factory.WithWebHostBuilder(b =>
        b.UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", desarrollo.ToString())).CreateClient(Sesion.Opciones);
    private IServiceScope Scope()
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();
        return scope;
    }
    private async Task<Datos> PrepararAsync(EstadoRuta estado = EstadoRuta.Despachada, int? desplazamiento = null,
        bool recursosPropios = false)
    {
        using var cliente = Cliente();
        (await cliente.GetAsync("/health")).EnsureSuccessStatusCode();
        using var scope = Scope();
        var ahora = DateTimeOffset.UtcNow;
        var hoy = DateOnly.FromDateTime(ahora.ToOffset(TimeSpan.FromHours(-3)).DateTime);
        var fecha = hoy.AddDays(desplazamiento ?? -20 - Interlocked.Increment(ref dia));
        var envio = Envio.Crear(DatosIniciales.OperadorDemoId, DatosIniciales.ComercioDemoId,
            $"ANULAR-{Guid.NewGuid():N}", new Destinatario("Prueba CU43", "099123456"),
            new Direccion("Calle", "10", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(1, 10, 10, 10, 0)], OrigenEvento.PortalComercio, null, ahora);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora);
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora);
        var envios = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        envios.Envios.Add(envio); await envios.SaveChangesAsync();
        var repartidor = DatosIniciales.RepartidorDemoId;
        var vehiculo = Guid.Parse("51111111-1111-1111-1111-111111111111");
        if (recursosPropios)
        {
            repartidor = Guid.NewGuid(); vehiculo = Guid.NewGuid();
            var admin = scope.ServiceProvider.GetRequiredService<AdministracionDbContext>();
            admin.Repartidores.Add(Logistica.Modules.Administracion.Domain.Planificacion.Repartidor.Crear(
                DatosIniciales.OperadorDemoId, "Anulación CU43", repartidor.ToString(), repartidor));
            admin.Vehiculos.Add(Logistica.Modules.Administracion.Domain.Planificacion.Vehiculo.Crear(
                DatosIniciales.OperadorDemoId, vehiculo.ToString("N"), "Furgón", 1000, 8, 400, 200, 200, vehiculo));
            await admin.SaveChangesAsync();
        }
        var ruta = Ruta.Crear(DatosIniciales.OperadorDemoId, fecha, repartidor,
            vehiculo, [new EnvioRuta(envio.Id, envio.Numero, fecha,
                Guid.Parse("61111111-1111-1111-1111-111111111111"), null, false,
                envio.Bultos.Select(b => new CargaBulto(envio.Id, b.Id, b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm)).ToArray())], ahora);
        var despacho = new DateTimeOffset(fecha.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        if (estado != EstadoRuta.Planificada) ruta.Despachar(despacho);
        if (estado == EstadoRuta.EnCurso) ruta.Iniciar();
        if (estado == EstadoRuta.Finalizada) ruta.Finalizar();
        var plan = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        plan.Rutas.Add(ruta); await plan.SaveChangesAsync();
        return new(ruta.Id, ruta.Revision, fecha, despacho, envio.Id, envio.Bultos.Single().Codigo, repartidor, vehiculo);
    }
    private static Task<HttpResponseMessage> AnularAsync(HttpClient client, Datos d, bool confirma = true,
        string motivo = "Coordinado con el repartidor; la ruta no salió.")
        => client.PostAsJsonAsync($"/api/planificacion/rutas/{d.Ruta}/despacho/anulacion",
            new AnularDespachoRequest(d.Revision, motivo, confirma));

    [Fact]
    public async Task Vuelve_a_planificada_conserva_envios_historial_outbox_y_libera_el_bloqueo_de_dias_siguientes()
    {
        var d = await PrepararAsync(recursosPropios: true); using var client = Cliente();
        using (var scope = Scope())
        {
            var ocupados = await scope.ServiceProvider.GetRequiredService<IRutaRepository>().RecursosOcupadosAsync(
                d.Fecha.AddDays(1), d.Repartidor, d.Vehiculo, null, default);
            Assert.Equal(2, ocupados.Count);
        }
        var respuesta = await AnularAsync(client, d); respuesta.EnsureSuccessStatusCode();
        var ruta = (await respuesta.Content.ReadFromJsonAsync<RutaResponse>())!;
        Assert.Equal("Planificada", ruta.Estado); Assert.Equal(d.Revision + 1, ruta.Revision); Assert.Null(ruta.DespachadaEn);
        Assert.Equal(d.Envio, Assert.Single(Assert.Single(ruta.Paradas).Envios).EnvioId);
        using (var scope = Scope())
        {
            var plan = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
            var auditoria = await plan.AnulacionesDespacho.SingleAsync(a => a.RutaId == d.Ruta);
            Assert.Equal(d.Despacho, auditoria.DespachadaEn); Assert.Equal(d.Revision, auditoria.RevisionAnterior);
            Assert.Equal(ruta.Revision, auditoria.RevisionRuta);
            Assert.Single(await plan.Outbox.Where(m => m.Tipo == "DespachoAnulado.v1" && m.Contenido.Contains(d.Ruta.ToString())).ToListAsync());
            Assert.Equal(EstadoEnvio.AsignadoARuta, (await scope.ServiceProvider.GetRequiredService<EnviosDbContext>()
                .Envios.SingleAsync(e => e.Id == d.Envio)).Estado);
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IRutaRepository>().RecursosOcupadosAsync(
                d.Fecha.AddDays(1), ruta.RepartidorId, ruta.VehiculoId, null, default));
            Assert.DoesNotContain(await scope.ServiceProvider.GetRequiredService<IPlanificacionModuleApi>()
                .ListarDelRepartidorAsync(ruta.RepartidorId, 1, default), r => r.Id == d.Ruta);
        }
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/backoffice/rutas/{d.Ruta}"));
        Assert.Contains("Despacho anulado", html); Assert.Contains("Coordinado con el repartidor", html);
        client.DefaultRequestHeaders.Add("RequestVerificationToken",
            Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var retiro = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,
            $"/api/planificacion/rutas/{d.Ruta}/envios/{d.Envio}") { Content = JsonContent.Create(new QuitarEnvioDeRutaRequest(ruta.Revision)) });
        retiro.EnsureSuccessStatusCode();
        Assert.Empty((await retiro.Content.ReadFromJsonAsync<RutaResponse>())!.Paradas);
    }

    [Theory]
    [InlineData("EnCurso", -10, 409)]
    [InlineData("Finalizada", -11, 409)]
    [InlineData("Planificada", -12, 409)]
    public async Task Rechaza_estados_que_no_admiten_reapertura(string estado, int fecha, int codigo)
    {
        var d = await PrepararAsync(Enum.Parse<EstadoRuta>(estado), fecha); using var client = Cliente();
        Assert.Equal(codigo, (int)(await AnularAsync(client, d)).StatusCode);
        using var scope = Scope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>().AnulacionesDespacho.AnyAsync(a => a.RutaId == d.Ruta));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Permite_volver_a_editar_un_despacho_de_hoy_o_futuro_con_motivo_y_sin_actividad(int desplazamiento)
    {
        var d = await PrepararAsync(desplazamiento: desplazamiento, recursosPropios: true);
        using var client = Cliente();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/backoffice/rutas/{d.Ruta}"));
        Assert.Contains("Volver a editar la ruta", html);
        Assert.Contains("Motivo de la anulación", html);
        client.DefaultRequestHeaders.Add("RequestVerificationToken",
            Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.Equal(HttpStatusCode.BadRequest, (await AnularAsync(client, d, motivo: " ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AnularAsync(client, d, confirma: false)).StatusCode);
        var respuesta = await AnularAsync(client, d, motivo: "Corregir la planificación antes de salir.");
        respuesta.EnsureSuccessStatusCode();
        var ruta = (await respuesta.Content.ReadFromJsonAsync<RutaResponse>())!;
        Assert.Equal("Planificada", ruta.Estado);
        Assert.Equal(d.Revision + 1, ruta.Revision);
        Assert.Null(ruta.DespachadaEn);
        using var scope = Scope();
        var plan = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        var auditoria = await plan.AnulacionesDespacho.SingleAsync(a => a.RutaId == d.Ruta);
        Assert.Equal("Corregir la planificación antes de salir.", auditoria.Motivo);
        Assert.Equal(d.Despacho, auditoria.DespachadaEn);
        // Sigue reservada ese día, pero deja de bloquear los días siguientes al volver a Planificada.
        var repositorio = scope.ServiceProvider.GetRequiredService<IRutaRepository>();
        Assert.Equal(2, (await repositorio.RecursosOcupadosAsync(d.Fecha, d.Repartidor, d.Vehiculo, null, default)).Count);
        Assert.Empty(await repositorio.RecursosOcupadosAsync(d.Fecha.AddDays(1), d.Repartidor, d.Vehiculo, null, default));
    }

    [Fact]
    public async Task La_ruta_reabierta_se_edita_con_las_mismas_restricciones_y_se_despacha_con_una_revision_nueva()
    {
        var d = await PrepararAsync(desplazamiento: 30, recursosPropios: true);
        using var client = Cliente();
        var cambio = new ModificarRutaRequest(d.Revision, d.Fecha.AddDays(1), d.Repartidor, d.Vehiculo);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/planificacion/rutas/{d.Ruta}/planificacion", cambio)).StatusCode);
        var reabierta = (await (await AnularAsync(client, d)).Content.ReadFromJsonAsync<RutaResponse>())!;
        Guid vehiculoChico;
        using (var scope = Scope())
        {
            var vehiculo = Logistica.Modules.Administracion.Domain.Planificacion.Vehiculo.Crear(
                DatosIniciales.OperadorDemoId, Guid.NewGuid().ToString("N"), "Prueba", 0.5m, 1, 100, 100, 100);
            vehiculoChico = vehiculo.Id;
            var admin = scope.ServiceProvider.GetRequiredService<AdministracionDbContext>();
            var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            var usuario = Usuario.CrearPersonal(DatosIniciales.OperadorDemoId, "Repartidor de reapertura", $"reapertura-{Guid.NewGuid():N}@prueba.uy");
            Assert.True((await usuarios.CreateAsync(usuario, UsuariosIniciales.ContrasenaDemo)).Succeeded);
            Assert.True((await usuarios.AddToRoleAsync(usuario, "Repartidor")).Succeeded);
            var repartidor = await admin.Repartidores.SingleAsync(r => r.Id == d.Repartidor);
            admin.Entry(repartidor).Property(r => r.UsuarioId).CurrentValue = usuario.Id;
            admin.Vehiculos.Add(vehiculo);
            await admin.SaveChangesAsync();
        }
        var invalida = await client.PutAsJsonAsync($"/api/planificacion/rutas/{d.Ruta}/planificacion",
            new ModificarRutaRequest(reabierta.Revision, d.Fecha.AddDays(1), d.Repartidor, vehiculoChico));
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        Assert.Contains("CapacidadPesoExcedida", await invalida.Content.ReadAsStringAsync());
        var modificacion = await client.PutAsJsonAsync($"/api/planificacion/rutas/{d.Ruta}/planificacion",
            cambio with { RevisionEsperada = reabierta.Revision });
        modificacion.EnsureSuccessStatusCode();
        var editada = (await modificacion.Content.ReadFromJsonAsync<RutaResponse>())!;
        Assert.Equal("Planificada", editada.Estado);
        Assert.Equal(d.Fecha.AddDays(1), editada.Fecha);
        var despacho = await client.PostAsJsonAsync($"/api/planificacion/rutas/{d.Ruta}/despacho", new DespacharRutaRequest(editada.Revision));
        despacho.EnsureSuccessStatusCode();
        var despachada = (await despacho.Content.ReadFromJsonAsync<RutaResponse>())!;
        Assert.Equal("Despachada", despachada.Estado);
        Assert.True(despachada.Revision > d.Revision);
        using var verificacion = Scope();
        var db = verificacion.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        Assert.Single(await db.AnulacionesDespacho.Where(a => a.RutaId == d.Ruta).ToListAsync());
        Assert.True(await db.Validaciones.AnyAsync(v => v.RutaId == d.Ruta && v.Operacion == OperacionValidacionRuta.Modificacion));
        Assert.True(await db.Validaciones.AnyAsync(v => v.RutaId == d.Ruta && v.Operacion == OperacionValidacionRuta.Despacho));
        Assert.Empty(await verificacion.ServiceProvider.GetRequiredService<IRutaRepository>().RecursosOcupadosAsync(
            d.Fecha, d.Repartidor, d.Vehiculo, null, default));
    }

    [Fact]
    public async Task Exige_motivo_confirmacion_y_revision_actual_y_no_duplica_dos_anulaciones_concurrentes()
    {
        var d = await PrepararAsync(); using var uno = Cliente(); using var dos = Cliente();
        Assert.Equal(HttpStatusCode.BadRequest, (await AnularAsync(uno, d, false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AnularAsync(uno, d, motivo: " ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AnularAsync(uno, d, motivo: new string('x', 1001))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await AnularAsync(uno, d with { Revision = d.Revision + 1 })).StatusCode);
        var resultados = await Task.WhenAll(AnularAsync(uno, d), AnularAsync(dos, d));
        Assert.Single(resultados, r => r.IsSuccessStatusCode); Assert.Single(resultados, r => r.StatusCode == HttpStatusCode.Conflict);
        using var scope = Scope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>().AnulacionesDespacho.Where(a => a.RutaId == d.Ruta).ToListAsync());
    }

    [Fact]
    public async Task Una_carga_aplicada_sin_envios_completos_tambien_impide_anular()
    {
        var d = await PrepararAsync(); using var scope = Scope();
        var ahora = DateTimeOffset.UtcNow;
        var db = scope.ServiceProvider.GetRequiredService<EjecucionDbContext>();
        db.Operaciones.Add(OperacionDispositivo.Aplicada(Guid.CreateVersion7(), new(DatosIniciales.OperadorDemoId,
            DatosIniciales.RepartidorDemoId, Guid.NewGuid(), 1, "ConfirmarCarga", d.Ruta, null, ahora, ahora, false)));
        await db.SaveChangesAsync();
        using var client = Cliente();
        var respuesta = await AnularAsync(client, d);
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Contains("RutaConActividad", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Carga_tardia_no_reactiva_la_ruta_anulada_y_conserva_el_conflicto()
    {
        var d = await PrepararAsync(); using var despacho = Cliente();
        (await AnularAsync(despacho, d)).EnsureSuccessStatusCode();
        using var client = Cliente(false); await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        var op = new OperacionDispositivoRequest(Guid.CreateVersion7(), Guid.NewGuid(), 1, "ConfirmarCarga", d.Ruta,
            null, d.Despacho, null, null, Carga: new(d.Revision, [new(d.Codigo, d.Despacho)]));
        var respuesta = await client.PostAsJsonAsync("/api/repartidor/sincronizacion", new SincronizacionRequest([op]));
        respuesta.EnsureSuccessStatusCode();
        var resultado = Assert.Single((await respuesta.Content.ReadFromJsonAsync<SincronizacionResponse>())!.Resultados);
        Assert.Equal("Conflicto", resultado.Resultado);
        using var scope = Scope();
        var operacion = await scope.ServiceProvider.GetRequiredService<EjecucionDbContext>().Operaciones.SingleAsync(o => o.Id == op.OperacionId);
        Assert.Equal(ResultadoOperacion.Conflicto, operacion.Resultado); Assert.False(operacion.Revisada); Assert.NotNull(operacion.Contenido);
        Assert.Equal(EstadoRuta.Planificada, (await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>().Rutas.SingleAsync(r => r.Id == d.Ruta)).Estado);
        Assert.Equal(EstadoEnvio.AsignadoARuta, (await scope.ServiceProvider.GetRequiredService<EnviosDbContext>().Envios.SingleAsync(e => e.Id == d.Envio)).Estado);
    }

    [Theory]
    [InlineData("Esperado")]
    [InlineData("Sobrante")]
    [InlineData("Rendicion")]
    public async Task Evidencia_existente_impide_anular_aunque_la_ruta_siga_despachada(string tipo)
    {
        var d = await PrepararAsync();
        using (var scope = Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EjecucionDbContext>();
            if (tipo == "Rendicion")
                db.Rendiciones.Add(Logistica.Modules.Ejecucion.Domain.Rendiciones.Rendicion.Iniciar(
                    DatosIniciales.OperadorDemoId, d.Ruta, d.Repartidor, Guid.NewGuid(), DateTimeOffset.UtcNow, null, []));
            else
                db.EscaneosCarga.Add(Logistica.Modules.Ejecucion.Domain.Cargas.EscaneoCarga.Crear(
                    DatosIniciales.OperadorDemoId, Guid.NewGuid(), d.Ruta, null, "CODIGO-PRUEBA", tipo == "Esperado", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        using var client = Cliente(); var respuesta = await AnularAsync(client, d);
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Contains("RutaConActividad", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Carga_y_anulacion_concurrentes_no_pueden_aplicarse_ambas()
    {
        var d = await PrepararAsync(); using var despacho = Cliente(); using var repartidor = Cliente(false);
        await Sesion.IngresarAsync(repartidor, "repartidor@operador-demo.uy");
        var op = new OperacionDispositivoRequest(Guid.CreateVersion7(), Guid.NewGuid(), 1, "ConfirmarCarga", d.Ruta,
            null, d.Despacho, null, null, Carga: new(d.Revision, [new(d.Codigo, d.Despacho)]));
        var anular = AnularAsync(despacho, d);
        var cargar = repartidor.PostAsJsonAsync("/api/repartidor/sincronizacion", new SincronizacionRequest([op]));
        await Task.WhenAll(anular, cargar);
        var carga = Assert.Single((await (await cargar).Content.ReadFromJsonAsync<SincronizacionResponse>())!.Resultados);
        using var scope = Scope(); var plan = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        var ruta = await plan.Rutas.SingleAsync(r => r.Id == d.Ruta);
        if ((await anular).IsSuccessStatusCode)
        {
            Assert.Equal("Conflicto", carga.Resultado); Assert.Equal(EstadoRuta.Planificada, ruta.Estado);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, (await anular).StatusCode);
            Assert.Equal("Aplicada", carga.Resultado); Assert.Equal(EstadoRuta.EnCurso, ruta.Estado);
        }
        Assert.Equal(d.Revision + 1, ruta.Revision);
    }

    [Fact]
    public async Task Si_un_envio_ya_salio_no_se_reabre_aunque_no_haya_operacion_en_ejecucion()
    {
        var d = await PrepararAsync();
        using (var scope = Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
            (await db.Envios.SingleAsync(e => e.Id == d.Envio)).Transicionar(EstadoEnvio.EnTransito,
                OrigenEvento.AppRepartidor, null, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        using var client = Cliente(); var respuesta = await AnularAsync(client, d);
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Contains("EnvioNoDisponible", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Formulario_exige_antiforgery_y_registra_el_responsable_de_despacho()
    {
        var d = await PrepararAsync(); using var client = Cliente(false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AnularAsync(client, d)).StatusCode);
        await Sesion.IngresarAsync(client, "despacho@operador-demo.uy");
        var url = $"/backoffice/rutas/{d.Ruta}";
        var html = await client.GetStringAsync(url);
        Assert.Contains("Confirmar anulación del despacho", WebUtility.HtmlDecode(html));
        var formulario = new Dictionary<string, string> { ["RevisionEsperada"] = d.Revision.ToString(),
            ["Fecha"] = d.Fecha.ToString("yyyy-MM-dd"), ["MotivoAnulacion"] = "Confirmado con el repartidor.", ["ConfirmoSinCargaPendiente"] = "true" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(url + "?handler=AnularDespacho", new FormUrlEncodedContent(formulario))).StatusCode);
        formulario["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var respuesta = await client.PostAsync(url + "?handler=AnularDespacho", new FormUrlEncodedContent(formulario));
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        using var scope = Scope();
        Assert.NotNull((await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>().AnulacionesDespacho.SingleAsync(a => a.RutaId == d.Ruta)).ResponsableId);
    }

    [Fact]
    public async Task Deposito_no_puede_anular_y_otro_operador_no_ve_la_ruta_ni_su_historial()
    {
        var d = await PrepararAsync(); using var deposito = Cliente(false);
        await Sesion.IngresarAsync(deposito, "deposito@operador-demo.uy");
        Assert.Equal(HttpStatusCode.Forbidden, (await AnularAsync(deposito, d)).StatusCode);
        using var ajeno = Cliente(false); await Sesion.IngresarAsync(ajeno, "despacho@envios-del-este.uy");
        // El token de antiforgery se obtiene de una pantalla propia, sin consultar la ruta ajena.
        var html = await ajeno.GetStringAsync("/backoffice/rutas/nueva");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        ajeno.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        Assert.Equal(HttpStatusCode.NotFound, (await AnularAsync(ajeno, d)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ajeno.GetAsync($"/api/planificacion/rutas/{d.Ruta}/anulaciones-despacho")).StatusCode);
    }

    private sealed class ConfirmacionQueFalla(IUnidadDeTrabajo unidad) : IConfirmacionPlanificacion
    {
        public Task EjecutarAsync(Func<Task> trabajo, IReadOnlyCollection<Guid> ids, CancellationToken ct)
            => unidad.EjecutarAsync(async () => { await trabajo(); throw new InvalidOperationException("Fallo antes del commit."); }, ct);
    }
    [Fact]
    public async Task Fallo_antes_del_commit_conserva_despacho_y_no_deja_auditoria_ni_outbox()
    {
        var d = await PrepararAsync();
        using var api = factory.WithWebHostBuilder(b => b.UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true")
            .ConfigureServices(s => s.AddScoped<IConfirmacionPlanificacion, ConfirmacionQueFalla>()));
        using var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.InternalServerError, (await AnularAsync(client, d)).StatusCode);
        using var scope = Scope(); var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        Assert.Equal(EstadoRuta.Despachada, (await db.Rutas.SingleAsync(r => r.Id == d.Ruta)).Estado);
        Assert.False(await db.AnulacionesDespacho.AnyAsync(a => a.RutaId == d.Ruta));
        Assert.False(await db.Outbox.AnyAsync(m => m.Tipo == "DespachoAnulado.v1" && m.Contenido.Contains(d.Ruta.ToString())));
    }
}

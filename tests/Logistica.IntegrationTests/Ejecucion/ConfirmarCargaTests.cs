using System.Net.Http.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using Logistica.Http.Contracts.Repartidor;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Ejecucion.Infrastructure.Persistence;
using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Ejecucion;

public sealed class ConfirmarCargaTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static int dia;
    private static readonly Guid Repartidor = DatosIniciales.RepartidorDemoId;
    private const string Traza = "00-123456789012345678901234567890ab-12345678901234ab-01";
    private sealed record Datos(Guid Ruta, long Revision, Envio[] Envios, DateOnly Fecha);
    private IServiceScope Scope()
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("operador_id", DatosIniciales.OperadorDemoId.ToString())], "Prueba")) };
        return scope;
    }
    private async Task<Datos> PrepararAsync(bool finalizada = false)
    {
        using var inicial = factory.CreateClient(); await inicial.GetAsync("/health");
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var plan = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        var ahora = DateTimeOffset.UtcNow.AddMinutes(-5);
        var fecha = new DateOnly(2025,1,1).AddDays(Interlocked.Increment(ref dia));
        var envios = Enumerable.Range(0,2).Select(i =>
        {
            var e=Envio.Crear(DatosIniciales.OperadorDemoId,DatosIniciales.ComercioDemoId,$"CU51-{Guid.NewGuid():N}",
                new Destinatario("Prueba Carga","099123456"),new Direccion("Calle Carga","10","Montevideo","Montevideo","11300"),
                i==0 ? [new DatosBulto(1,10,10,10,0),new DatosBulto(1,10,10,10,0)] : [new DatosBulto(1,10,10,10,0)],
                OrigenEvento.PortalComercio,null,ahora);
            e.Transicionar(EstadoEnvio.EnDeposito,OrigenEvento.Backoffice,null,ahora);
            e.Transicionar(EstadoEnvio.AsignadoARuta,OrigenEvento.Backoffice,null,ahora);
            return e;
        }).ToArray();
        db.Envios.AddRange(envios); await db.SaveChangesAsync();
        var ruta=Ruta.Crear(DatosIniciales.OperadorDemoId,fecha,Repartidor,Guid.Parse("51111111-1111-1111-1111-111111111111"),
            envios.Select(e => new EnvioRuta(e.Id,e.Numero,fecha,Guid.Parse("61111111-1111-1111-1111-111111111111"),null,false,
                e.Bultos.Select(b => new CargaBulto(e.Id,b.Id,b.PesoKg,b.LargoCm,b.AnchoCm,b.AltoCm)).ToArray(),
                new PuntoEntrega("Calle Carga","10","Montevideo","Montevideo","11300"))).ToArray(),ahora);
        ruta.Despachar(ahora); if(finalizada) ruta.Finalizar();
        plan.Rutas.Add(ruta); await plan.SaveChangesAsync();
        await DatosFisicosRf34.AcreditarAsync(db, plan, ruta, envios, ahora);
        return new(ruta.Id,ruta.Revision,envios,fecha);
    }
    private HttpClient Cliente() => factory.WithWebHostBuilder(b=>b.UseSetting("Identity:PermitirTenantProvisorio","false")).CreateClient(Sesion.Opciones);

    [Fact]
    public async Task La_carga_nueva_no_inicia_la_ruta_y_el_inicio_cierra_carga_sin_duplicar_al_reenviar()
    {
        var d = await PrepararAsync();
        using var cliente = Cliente();
        await Sesion.IngresarAsync(cliente, "repartidor@operador-demo.uy");
        var original = Operacion(d, [d.Envios[0].Bultos[0].Codigo, d.Envios[1].Bultos[0].Codigo], acepta:true);
        var carga = original with { Carga = original.Carga! with { PorBulto = true, Faltantes = [] } };
        Assert.Equal("Aplicada", (await Enviar(cliente, carga)).Resultado);
        using (var scope = Scope())
            Assert.Equal(EstadoRuta.Despachada, (await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>()
                .Rutas.SingleAsync(r => r.Id == d.Ruta)).Estado);
        var visitaAntes = carga with { OperacionId = Guid.CreateVersion7(), Tipo = "RegistrarVisita", Carga = null,
            EnvioId = d.Envios[1].Id, Latitud = -34.9m, Longitud = -56.1m,
            Visita = new([new(d.Envios[1].Bultos[0].Codigo)]) };
        Assert.Equal("RutaNoIniciada", (await Enviar(cliente, visitaAntes)).Motivo);

        var inicio = carga with { OperacionId = Guid.CreateVersion7(), Tipo = "IniciarRuta", Carga = null, Secuencia = 2 };
        var resultados = await Task.WhenAll(Enviar(cliente, inicio), Enviar(cliente, inicio));
        Assert.Contains(resultados, r => r.Resultado == "Aplicada");
        Assert.Contains(resultados, r => r.Resultado == "YaAplicada");
        var tardia = Operacion(d, d.Envios[0].Bultos.Select(b => b.Codigo), acepta:true);
        tardia = tardia with { Carga = tardia.Carga! with { PorBulto = true, Faltantes = [] } };
        Assert.Equal("RutaYaIniciada", (await Enviar(cliente, tardia)).Motivo);
        Assert.Equal("YaAplicada", (await Enviar(cliente, carga)).Resultado);
        using var final = Scope();
        var ruta = await final.ServiceProvider.GetRequiredService<PlanificacionDbContext>().Rutas.SingleAsync(r => r.Id == d.Ruta);
        Assert.Equal(EstadoRuta.EnCurso, ruta.Estado);
        Assert.Equal(d.Revision + 2, ruta.Revision); // una carga y un inicio, sin duplicados
        var envio = await final.ServiceProvider.GetRequiredService<EnviosDbContext>().Envios.SingleAsync(e => e.Id == d.Envios[0].Id);
        Assert.All(envio.Bultos, b => Assert.Equal(EstadoEnvio.AsignadoARuta, b.Estado));
        Assert.Single(await final.ServiceProvider.GetRequiredService<EjecucionDbContext>().Operaciones.Where(o => o.Id == inicio.OperacionId).ToListAsync());
    }

    [Fact]
    public async Task El_lote_aplica_carga_antes_de_inicio_y_un_rechazo_de_carga_bloquea_el_inicio()
    {
        var d = await PrepararAsync();
        using var cliente = Cliente();
        await Sesion.IngresarAsync(cliente, "repartidor@operador-demo.uy");
        var carga = Operacion(d);
        carga = carga with { Carga = carga.Carga! with { PorBulto = true, Faltantes = [] } };
        var inicio = carga with { OperacionId = Guid.CreateVersion7(), Tipo = "IniciarRuta", Carga = null, Secuencia = 2 };
        var respuesta = await cliente.PostAsJsonAsync("/api/repartidor/sincronizacion", new SincronizacionRequest([inicio, carga]));
        respuesta.EnsureSuccessStatusCode();
        Assert.All((await respuesta.Content.ReadFromJsonAsync<SincronizacionResponse>())!.Resultados, r => Assert.Equal("Aplicada", r.Resultado));
        var otro = await PrepararAsync();
        var invalida = Operacion(otro);
        invalida = invalida with { Carga = invalida.Carga! with { RevisionRuta = 999, PorBulto = true, Faltantes = [] } };
        var dependiente = invalida with { OperacionId = Guid.CreateVersion7(), Tipo = "IniciarRuta", Carga = null, Secuencia = 2 };
        respuesta = await cliente.PostAsJsonAsync("/api/repartidor/sincronizacion", new SincronizacionRequest([invalida, dependiente]));
        respuesta.EnsureSuccessStatusCode();
        var rechazadas = (await respuesta.Content.ReadFromJsonAsync<SincronizacionResponse>())!.Resultados;
        Assert.Equal("RutaModificada", rechazadas[0].Motivo);
        Assert.Equal("DependeDeOperacionRechazada", rechazadas[1].Motivo);
        // Un lote posterior tampoco puede ignorar la carga rechazada de un lote anterior.
        var corregida = Operacion(otro);
        corregida = corregida with { Carga = corregida.Carga! with { PorBulto = true, Faltantes = [] } };
        Assert.Equal("Aplicada", (await Enviar(cliente, corregida)).Resultado);
        Assert.Equal("DependeDeOperacionRechazada", (await Enviar(cliente,
            dependiente with { OperacionId = Guid.CreateVersion7() })).Motivo);
        using var scope = Scope();
        Assert.Equal(EstadoRuta.Despachada, (await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>()
            .Rutas.SingleAsync(r => r.Id == otro.Ruta)).Estado);
    }

    [Fact]
    public async Task No_se_puede_comenzar_sin_carga_ni_desde_otro_operador()
    {
        var d = await PrepararAsync();
        using var cliente = Cliente();
        await Sesion.IngresarAsync(cliente, "repartidor@operador-demo.uy");
        var inicio = Operacion(d) with { Tipo = "IniciarRuta", Carga = null };
        Assert.Equal("SinCargaConfirmada", (await Enviar(cliente, inicio)).Motivo);
        using var ajeno = Cliente();
        await Sesion.IngresarAsync(ajeno, "repartidor@envios-del-este.uy");
        Assert.Equal("RutaNoAsignada", (await Enviar(ajeno, inicio with { OperacionId = Guid.CreateVersion7() })).Motivo);
        using var scope = Scope();
        var ruta = await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>().Rutas.SingleAsync(r => r.Id == d.Ruta);
        Assert.Equal(EstadoRuta.Despachada, ruta.Estado);
        Assert.Equal(d.Revision, ruta.Revision);
    }
    [Fact]
    public async Task Por_bulto_un_envio_sin_parcial_no_pide_motivos_ni_bloquea_otro_completo()
    {
        var d=await PrepararAsync();using var cliente=Cliente();await Sesion.IngresarAsync(cliente,"repartidor@operador-demo.uy");
        var original=Operacion(d,[d.Envios[0].Bultos[0].Codigo,d.Envios[1].Bultos[0].Codigo],acepta:true);
        var op=original with { Carga=original.Carga! with { PorBulto=true,Faltantes=[] } };
        Assert.Equal("Aplicada",(await Enviar(cliente,op)).Resultado);
        Assert.Equal("YaAplicada",(await Enviar(cliente,op)).Resultado);
        using var scope=Scope();var db=scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var bloqueado=await db.Envios.SingleAsync(e=>e.Id==d.Envios[0].Id);
        Assert.All(bloqueado.Bultos,b=>Assert.Equal(EstadoEnvio.AsignadoARuta,b.Estado));
        var completo=await db.Envios.SingleAsync(e=>e.Id==d.Envios[1].Id);
        Assert.All(completo.Bultos,b=>Assert.Equal(EstadoEnvio.EnTransito,b.Estado));
        // Se escaneó un bulto del bloqueado, pero ninguno viajó. No exige recibirlo en la rendición nueva.
        var rendicion = await scope.ServiceProvider.GetRequiredService<Logistica.Modules.Ejecucion.Application.Features.IniciarRendicion.PrepararRendicion>()
            .ConsultarAsync(d.Ruta, d.Envios.Select(e => e.Id).ToArray(), default);
        Assert.All(rendicion.Bultos.Where(b => b.EnvioId == bloqueado.Id), b =>
        {
            Assert.Equal("NoCargado", b.Situacion);
            Assert.False(b.RequiereDeclaracion);
        });
        Assert.All(rendicion.Bultos.Where(b => b.EnvioId == completo.Id), b => Assert.Equal("NoIntentado", b.Situacion));
    }
    [Fact]
    public async Task Por_bulto_sin_ningun_envio_cargable_se_rechaza_aunque_acepte_faltantes()
    {
        var d=await PrepararAsync();using var cliente=Cliente();await Sesion.IngresarAsync(cliente,"repartidor@operador-demo.uy");
        var original=Operacion(d,[d.Envios[0].Bultos[0].Codigo],acepta:true);
        var op=original with { Carga=original.Carga! with { PorBulto=true,Faltantes=[] } };
        Assert.Equal("CargaIncompletaNoAdmitida",(await Enviar(cliente,op)).Motivo);
        using var scope=Scope();var db=scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var envios=await db.Envios.Where(e=>d.Envios.Select(x=>x.Id).Contains(e.Id)).ToListAsync();
        Assert.All(envios.SelectMany(e=>e.Bultos),b=>Assert.Equal(EstadoEnvio.AsignadoARuta,b.Estado));
    }
    private static OperacionDispositivoRequest Operacion(Datos d, IEnumerable<string>? codigos = null, bool acepta = false)
    {
        var ahora=new DateTimeOffset(d.Fecha.ToDateTime(new TimeOnly(15,0)),TimeSpan.Zero);
        return new(Guid.CreateVersion7(),Guid.NewGuid(),1,"ConfirmarCarga",d.Ruta,null,ahora,null,null,Traza,
            Carga:new(d.Revision,(codigos ?? d.Envios.SelectMany(e=>e.Bultos).Select(b=>b.Codigo))
                .Select(c=>new LecturaCargaRequest(c,ahora.AddSeconds(-1))).ToArray(),acepta));
    }
    private static async Task<ResultadoOperacionResponse> Enviar(HttpClient client,OperacionDispositivoRequest op)
    {
        var response=await client.PostAsJsonAsync("/api/repartidor/sincronizacion",new SincronizacionRequest([op]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SincronizacionResponse>())!.Resultados.Single();
    }

    private sealed class OperacionQueFallaAlFinal(EjecucionDbContext db) : IOperacionRepository
    {
        private readonly OperacionRepository original = new(db);
        private bool aplicada;
        public Task<OperacionDispositivo?> ObtenerAsync(Guid id, CancellationToken ct) => original.ObtenerAsync(id, ct);
        public Task<IReadOnlyList<OperacionDispositivo>> ConflictosPendientesAsync(CancellationToken ct) => original.ConflictosPendientesAsync(ct);
        public Task<OperacionDispositivo?> ObtenerParaRevisarAsync(Guid id, CancellationToken ct) => original.ObtenerParaRevisarAsync(id, ct);
        public void Agregar(OperacionDispositivo operacion)
        {
            aplicada = operacion.Resultado == ResultadoOperacion.Aplicada;
            original.Agregar(operacion);
        }
        public async Task GuardarCambiosAsync(CancellationToken ct)
        {
            await original.GuardarCambiosAsync(ct);
            if (aplicada) throw new InvalidOperationException("Fallo de prueba después del último guardado, antes del commit.");
        }
    }

    [Fact]
    public async Task La_fuente_de_trazas_exportable_continua_el_contexto_guardado_y_lo_transmite_al_outbox()
    {
        var d = await PrepararAsync();
        using var client = Cliente();
        await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        var traceId = Guid.NewGuid().ToString("N");
        var op = Operacion(d) with { CorrelacionId = $"00-{traceId}-12345678901234ab-01" };
        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Logistica.Ejecucion",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { if (activity.TraceId.ToString() == traceId) spans.Add(activity); }
        };
        ActivitySource.AddActivityListener(listener);

        Assert.Equal("Aplicada", (await Enviar(client, op)).Resultado);
        var span = Assert.Single(spans);
        Assert.Equal("operacion.procesar", span.OperationName);
        Assert.Equal(ActivityKind.Consumer, span.Kind);
        Assert.Equal("12345678901234ab", span.ParentSpanId.ToString());
        Assert.True(span.Recorded);
        using var scope = Scope();
        var ids = d.Envios.Select(e => e.Id).ToArray();
        var outbox = await scope.ServiceProvider.GetRequiredService<EnviosDbContext>().Outbox
            .Where(m => m.Tipo == "EnvioEnTransito.v1" && ids.Any(id => m.Contenido.Contains(id.ToString())))
            .ToListAsync();
        Assert.Equal(2, outbox.Count);
        Assert.All(outbox, m => Assert.Equal(span.Id, m.TraceParent));
    }

    [Fact]
    public async Task Un_fallo_antes_del_commit_revierte_los_tres_modulos_y_permite_reintentar_la_misma_operacion()
    {
        var d = await PrepararAsync();
        using var apiConFallo = factory.WithWebHostBuilder(b => b
            .UseSetting("Identity:PermitirTenantProvisorio", "false")
            .ConfigureServices(s => s.AddScoped<IOperacionRepository, OperacionQueFallaAlFinal>()));
        using var client = apiConFallo.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        var op = Operacion(d, d.Envios[0].Bultos.Select(b => b.Codigo).Append("SOBRANTE-ROLLBACK"), true);
        var response = await client.PostAsJsonAsync("/api/repartidor/sincronizacion", new SincronizacionRequest([op]));
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);

        using (var scope = Scope())
        {
            var ejecucion = scope.ServiceProvider.GetRequiredService<EjecucionDbContext>();
            Assert.False(await ejecucion.Operaciones.AnyAsync(o => o.Id == op.OperacionId));
            Assert.False(await ejecucion.EscaneosCarga.AnyAsync(e => e.OperacionId == op.OperacionId));
            Assert.False(await ejecucion.Outbox.AnyAsync(m => m.Contenido.Contains(op.OperacionId.ToString())));
            var ids = d.Envios.Select(e => e.Id).ToArray();
            var envios = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
            Assert.All(await envios.Envios.Where(e => ids.Contains(e.Id)).ToListAsync(),
                e => Assert.Equal(EstadoEnvio.AsignadoARuta, e.Estado));
            Assert.False(await envios.Outbox.AnyAsync(m => m.Tipo == "EnvioEnTransito.v1"
                && ids.Any(id => m.Contenido.Contains(id.ToString()))));
            var ruta = await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>().Rutas.SingleAsync(r => r.Id == d.Ruta);
            Assert.Equal(EstadoRuta.Despachada, ruta.Estado);
            Assert.Equal(d.Revision, ruta.Revision);
        }

        using var recuperado = Cliente();
        await Sesion.IngresarAsync(recuperado, "repartidor@operador-demo.uy");
        Assert.Equal("Aplicada", (await Enviar(recuperado, op)).Resultado);
        Assert.Equal("YaAplicada", (await Enviar(recuperado, op)).Resultado);
    }
    [Fact]
    public async Task Completa_aplica_T5_inicia_ruta_y_outbox_con_la_traza_del_dispositivo_sin_duplicar()
    {
        var d=await PrepararAsync(); using var client=Cliente(); await Sesion.IngresarAsync(client,"repartidor@operador-demo.uy");
        var op=Operacion(d);
        Assert.Equal("Aplicada",(await Enviar(client,op)).Resultado);
        Assert.Equal("YaAplicada",(await Enviar(client,op)).Resultado);
        using var scope=Scope();
        var db=scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var ids=d.Envios.Select(e=>e.Id).ToArray();
        Assert.All(await db.Envios.Where(e=>ids.Contains(e.Id)).ToListAsync(),e=>Assert.Equal(EstadoEnvio.EnTransito,e.Estado));
        var mensajes=await db.Outbox.Where(m=>m.Tipo=="EnvioEnTransito.v1" && ids.Any(id=>m.Contenido.Contains(id.ToString()))).ToListAsync();
        Assert.Equal(2,mensajes.Count);
        Assert.All(mensajes,m=>Assert.Contains("123456789012345678901234567890ab",m.TraceParent!));
        Assert.Equal(EstadoRuta.EnCurso,(await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>().Rutas.SingleAsync(r=>r.Id==d.Ruta)).Estado);
        Assert.Equal(3,await scope.ServiceProvider.GetRequiredService<EjecucionDbContext>().EscaneosCarga.CountAsync(e=>e.OperacionId==op.OperacionId));
    }
    [Fact]
    public async Task Con_faltantes_solo_carga_envios_completos_y_guarda_sobrante_desconocido_sin_bulto_ficticio()
    {
        var d=await PrepararAsync(); using var client=Cliente(); await Sesion.IngresarAsync(client,"repartidor@operador-demo.uy");
        var op=Operacion(d,d.Envios[0].Bultos.Select(b=>b.Codigo).Append("SOBRANTE-DESCONOCIDO"),true);
        Assert.Equal("Aplicada",(await Enviar(client,op)).Resultado);
        using var scope=Scope(); var db=scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        Assert.Equal(EstadoEnvio.EnTransito,(await db.Envios.SingleAsync(e=>e.Id==d.Envios[0].Id)).Estado);
        Assert.Equal(EstadoEnvio.AsignadoARuta,(await db.Envios.SingleAsync(e=>e.Id==d.Envios[1].Id)).Estado);
        var ejecucion=scope.ServiceProvider.GetRequiredService<EjecucionDbContext>();
        var sobrante=await ejecucion.EscaneosCarga.SingleAsync(e=>e.OperacionId==op.OperacionId && e.Resultado=="Sobrante");
        Assert.Null(sobrante.BultoId);
        Assert.True(await ejecucion.Outbox.AnyAsync(m=>m.Tipo=="CargaConDiscrepancias.v1" && m.Contenido.Contains(op.OperacionId.ToString())));
    }
    [Theory]
    [InlineData("repetido","CargaInvalida")]
    [InlineData("faltantes","FaltantesSinConfirmar")]
    [InlineData("revision","RutaModificada")]
    [InlineData("finalizada","RutaNoAsignada")]
    [InlineData("estado","EstadoIncompatible")]
    [InlineData("otro-operador","RutaNoAsignada")]
    public async Task Rechaza_sin_escaneos_ni_transiciones_parciales(string caso,string motivo)
    {
        var d=await PrepararAsync(caso=="finalizada"); using var client=Cliente();
        await Sesion.IngresarAsync(client,caso=="otro-operador"?"repartidor@envios-del-este.uy":"repartidor@operador-demo.uy");
        var op=Operacion(d);
        if(caso=="repetido") op=op with { Carga=op.Carga! with { Lecturas=[op.Carga.Lecturas[0],op.Carga.Lecturas[0]] } };
        if(caso=="faltantes") op=Operacion(d,[d.Envios[0].Bultos[0].Codigo]);
        if(caso=="revision") op=op with { Carga=op.Carga! with { RevisionRuta=d.Revision+1 } };
        if(caso=="estado")
        {
            using var preparar=Scope(); var db=preparar.ServiceProvider.GetRequiredService<EnviosDbContext>();
            var envio=await db.Envios.SingleAsync(e=>e.Id==d.Envios[0].Id);
            envio.Transicionar(EstadoEnvio.EnTransito,OrigenEvento.AppRepartidor,null,DateTimeOffset.UtcNow); await db.SaveChangesAsync();
        }
        var resultado=await Enviar(client,op); Assert.Equal(motivo,resultado.Motivo);
        using var scope=Scope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<EjecucionDbContext>().EscaneosCarga.AnyAsync(e=>e.OperacionId==op.OperacionId));
        Assert.Equal(EstadoEnvio.AsignadoARuta,(await scope.ServiceProvider.GetRequiredService<EnviosDbContext>().Envios.SingleAsync(e=>e.Id==d.Envios[1].Id)).Estado);
    }
    [Fact]
    public async Task Dos_reenvios_simultaneos_confirman_una_sola_vez()
    {
        var d=await PrepararAsync(); using var uno=Cliente(); using var dos=Cliente();
        await Sesion.IngresarAsync(uno,"repartidor@operador-demo.uy"); await Sesion.IngresarAsync(dos,"repartidor@operador-demo.uy");
        var op=Operacion(d); var resultados=await Task.WhenAll(Enviar(uno,op),Enviar(dos,op));
        Assert.Contains(resultados,r=>r.Resultado=="Aplicada"); Assert.Contains(resultados,r=>r.Resultado=="YaAplicada");
        using var scope=Scope(); Assert.Equal(3,await scope.ServiceProvider.GetRequiredService<EjecucionDbContext>().EscaneosCarga.CountAsync(e=>e.OperacionId==op.OperacionId));
    }

    [Theory]
    [InlineData(-1, 15, false)]
    [InlineData(1, 15, false)]
    [InlineData(1, 1, true)]
    [InlineData(0, 1, false)]
    public async Task La_carga_exige_su_fecha_en_la_zona_del_operador_pero_admite_sincronizar_despues(int dias, int horaUtc, bool valida)
    {
        var d = await PrepararAsync(); using var client = Cliente();
        await Sesion.IngresarAsync(client,"repartidor@operador-demo.uy");
        var momento = new DateTimeOffset(d.Fecha.AddDays(dias).ToDateTime(new TimeOnly(horaUtc,0)),TimeSpan.Zero);
        var op = Operacion(d) with { OcurridoEn = momento };
        op = op with { Carga = op.Carga! with { Lecturas = op.Carga.Lecturas.Select(l => l with { EscaneadoEn = momento.AddSeconds(-1) }).ToArray() } };
        var resultado = await Enviar(client,op);
        Assert.Equal(valida ? "Aplicada" : "Conflicto",resultado.Resultado);
        if (valida) Assert.Equal("YaAplicada",(await Enviar(client,op)).Resultado);
        else
        {
            Assert.Equal("FechaDeCargaInvalida",resultado.Motivo);
            using var scope = Scope();
            Assert.False(await scope.ServiceProvider.GetRequiredService<EjecucionDbContext>().EscaneosCarga.AnyAsync(e => e.OperacionId == op.OperacionId));
            var ids = d.Envios.Select(e => e.Id).ToArray();
            Assert.All(await scope.ServiceProvider.GetRequiredService<EnviosDbContext>().Envios.Where(e => ids.Contains(e.Id)).ToListAsync(), e => Assert.Equal(EstadoEnvio.AsignadoARuta,e.Estado));
        }
    }
}

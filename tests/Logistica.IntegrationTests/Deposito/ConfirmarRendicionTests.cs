using System.Net;
using System.Security.Claims;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Ejecucion.Domain.Rendiciones;
using Logistica.Modules.Ejecucion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Domain.Incidencias;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Deposito;

// CU-55 de punta a punta, desde la página del operario de depósito: Depósito coordina, Ejecución cierra la rendición,
// Envíos reintegra (T8) y registra los faltantes, y Planificación finaliza la ruta, todo en una transacción.
public sealed class ConfirmarRendicionTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static int dia;

    // Una ruta en curso con su rendición iniciada (CU-54): un envío no intentado que vuelve entero, otro no intentado
    // al que le va a faltar un bulto y uno que se entregó en la calle.
    private sealed record Escenario(Guid Ruta, Envio NoIntentado, Envio ConFaltante, Envio Entregado);

    [Fact]
    public async Task Todos_entregados_solo_permite_cierre_administrativo_y_confirma_sin_escanear()
    {
        var e = await PrepararAsync(todosEntregados: true);
        using var client = await OperarioAsync();
        var pagina = $"/backoffice/rendiciones/{e.Ruta}";
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(pagina));
        Assert.Contains("No hay bultos para recibir en depósito", html);
        Assert.Contains("Confirmar cierre de ruta", html);
        Assert.DoesNotContain("<textarea", html);
        Assert.DoesNotContain("handler=Comparar", html);

        // El formulario no envía Escaneados cuando no hay recepción física.
        var respuesta = await client.PostAsync($"{pagina}?handler=Confirmar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await Sesion.TokenAsync(client, pagina),
        }));
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal("/backoffice/rendiciones", respuesta.Headers.Location?.OriginalString);
        using var scope = Scope();
        var rendicion = await scope.ServiceProvider.GetRequiredService<EjecucionDbContext>().Rendiciones
            .Include(r => r.Lineas).SingleAsync(r => r.RutaId == e.Ruta);
        Assert.NotNull(rendicion.FinalizadaEn);
        Assert.All(rendicion.Lineas, l =>
        {
            Assert.Null(l.Recibida);
            Assert.Equal(EstadoLineaRendicion.EntregadoEnCalle, l.EstadoFinal);
        });
        var ruta = await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>().Rutas
            .Include(r => r.Paradas).ThenInclude(p => p.Envios).SingleAsync(r => r.Id == e.Ruta);
        Assert.Equal(EstadoRuta.Finalizada, ruta.Estado);
        Assert.False(ruta.ReservaActiva);
        Assert.All(ruta.Paradas, p => Assert.Equal(EstadoParada.Completada, p.Estado));
        Assert.All(ruta.Paradas.SelectMany(p => p.Envios), envio => Assert.Equal(EstadoParada.Completada, envio.Estado));
        var envios = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        foreach (var envio in new[] { e.NoIntentado, e.ConFaltante, e.Entregado })
        {
            Assert.Equal(EstadoEnvio.Entregado, await EstadoAsync(envios, envio.Id));
            Assert.False(await envios.Incidencias.AnyAsync(i => i.EnvioId == envio.Id));
        }
    }

    [Fact]
    public async Task Todos_entregados_rechaza_codigos_forzados_sin_recibir_bultos_ni_cerrar()
    {
        var e = await PrepararAsync(todosEntregados: true);
        using var client = await OperarioAsync();
        var respuesta = await ConfirmarAsync(client, e.Ruta, e.Entregado.Bultos[0].Codigo);
        Assert.Equal($"/backoffice/rendiciones/{e.Ruta}", respuesta.Headers.Location?.OriginalString);
        using var scope = Scope();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<EjecucionDbContext>().Rendiciones
            .SingleAsync(r => r.RutaId == e.Ruta)).FinalizadaEn);
        Assert.Equal(EstadoRuta.EnCurso, (await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>().Rutas
            .SingleAsync(r => r.Id == e.Ruta)).Estado);
    }

    [Fact]
    public async Task Confirmar_reintegra_los_no_intentados_registra_los_faltantes_y_finaliza_la_ruta()
    {
        var e = await PrepararAsync();
        using var client = await OperarioAsync();
        // Uno escrito en mayúsculas y con espacios: el lector no siempre respeta el formato del código.
        var recibidos = e.NoIntentado.Bultos.Select(b => b.Codigo)
            .Append($"  {e.ConFaltante.Bultos[0].Codigo.ToUpperInvariant()} ").ToArray();

        var respuesta = await ConfirmarAsync(client, e.Ruta, recibidos);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal("/backoffice/rendiciones", respuesta.Headers.Location?.OriginalString);

        using var scope = Scope();
        var envios = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        Assert.Equal(EstadoEnvio.EnDeposito, await EstadoAsync(envios, e.NoIntentado.Id));
        Assert.Equal(EstadoEnvio.EnTransito, await EstadoAsync(envios, e.ConFaltante.Id));
        Assert.Equal(EstadoEnvio.Entregado, await EstadoAsync(envios, e.Entregado.Id));

        // T8 sale por el Outbox, para el seguimiento y los avisos.
        Assert.Single(await envios.Outbox.Where(m => m.Tipo == EnvioReintegradoADeposito.Tipo
            && m.Contenido.Contains(e.NoIntentado.Id.ToString())).ToListAsync());

        // A1: el faltante queda como incidencia del envío, con el código del bulto; el envío no se declara extraviado.
        var incidencia = Assert.Single(await envios.Incidencias.Where(i => i.EnvioId == e.ConFaltante.Id).ToListAsync());
        Assert.Equal(TipoIncidencia.Extravio, incidencia.Tipo);
        Assert.Contains(e.ConFaltante.Bultos[1].Codigo, incidencia.Descripcion);
        Assert.Empty(await envios.Incidencias.Where(i => i.EnvioId == e.NoIntentado.Id).ToListAsync());

        var rendicion = await scope.ServiceProvider.GetRequiredService<EjecucionDbContext>().Rendiciones
            .Include(r => r.Lineas).SingleAsync(r => r.RutaId == e.Ruta);
        Assert.NotNull(rendicion.FinalizadaEn);
        Assert.Equal(3, rendicion.Lineas.Count(l => l.EstadoFinal == EstadoLineaRendicion.DevueltoADeposito));
        Assert.Single(rendicion.Lineas, l => l.Recibida == false && l.EstadoFinal is null);
        Assert.Equal(2, rendicion.Lineas.Count(l => l.EstadoFinal == EstadoLineaRendicion.EntregadoEnCalle));

        // La ruta termina y libera al repartidor y al vehículo.
        var ruta = await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>().Rutas
            .Include(r => r.Paradas).ThenInclude(p => p.Envios).SingleAsync(r => r.Id == e.Ruta);
        Assert.Equal(EstadoRuta.Finalizada, ruta.Estado);
        Assert.False(ruta.ReservaActiva);
        Assert.All(ruta.Paradas.SelectMany(p => p.Envios), envio => Assert.Equal(
            envio.EnvioId == e.Entregado.Id ? EstadoParada.Completada : EstadoParada.Fallida, envio.Estado));
        Assert.All(ruta.Paradas, p => Assert.Equal(EstadoParada.Fallida, p.Estado));

        using var despacho = await OperarioAsync("despacho@operador-demo.uy");
        var detalleRuta = WebUtility.HtmlDecode(await despacho.GetStringAsync($"/backoffice/rutas/{e.Ruta}"));
        Assert.Contains("data-finalizacion-ruta", detalleRuta);
        Assert.Contains("rendición confirmada en depósito", detalleRuta);
        Assert.Contains(rendicion.FinalizadaEn!.Value.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy HH:mm"), detalleRuta);

        // Ya no está pendiente, y la página la muestra confirmada, sin el formulario.
        Assert.DoesNotContain(e.Ruta.ToString()[^8..], await client.GetStringAsync("/backoffice/rendiciones"));
        Assert.DoesNotContain("Confirmar la rendici", await client.GetStringAsync($"/backoffice/rendiciones/{e.Ruta}"));
    }

    [Fact]
    public async Task El_detalle_reconstruye_cierres_historicos_sin_escribir_ni_consultar_el_estado_actual_del_envio()
    {
        var e = await PrepararAsync();
        using var deposito = await OperarioAsync();
        await ConfirmarAsync(deposito, e.Ruta, e.NoIntentado.Bultos.Select(b => b.Codigo).ToArray());
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        var ruta = await db.Rutas.Include(r => r.Paradas).ThenInclude(p => p.Envios).SingleAsync(r => r.Id == e.Ruta);
        // Datos que dejó la versión anterior: ruta finalizada con sus miembros pendientes.
        foreach (var parada in ruta.Paradas)
        {
            db.Entry(parada).Property(p => p.Estado).CurrentValue = EstadoParada.Pendiente;
            foreach (var envio in parada.Envios) db.Entry(envio).Property(p => p.Estado).CurrentValue = EstadoParada.Pendiente;
        }
        await db.SaveChangesAsync();
        var lector = scope.ServiceProvider.GetRequiredService<Logistica.Modules.Planificacion.Application.Features.ConsultarRutas.ConsultarRutasHandler>();
        var detalle = await lector.DetalleAsync(e.Ruta, default);
        Assert.NotNull(detalle.FinalizadaEn);
        Assert.All(detalle.Paradas.SelectMany(p => p.Envios), envio => Assert.Equal(
            envio.EnvioId == e.Entregado.Id ? "Completada" : "Fallida", envio.Estado));
        Assert.All(db.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    [Fact]
    public async Task Un_sobrante_impide_confirmar_y_no_cambia_nada()
    {
        var e = await PrepararAsync();
        using var client = await OperarioAsync();
        var escaneados = e.NoIntentado.Bultos.Select(b => b.Codigo)
            .Append(e.Entregado.Bultos[0].Codigo).Append("NO-EXISTE-1").ToArray();

        var respuesta = await ConfirmarAsync(client, e.Ruta, escaneados);

        // A2: vuelve a la rendición con el error y la lista escaneada, para revisarla.
        Assert.Equal($"/backoffice/rendiciones/{e.Ruta}", respuesta.Headers.Location?.OriginalString);
        var html = await client.GetStringAsync(respuesta.Headers.Location!.OriginalString);
        Assert.Contains("Hay bultos que no", html);
        Assert.Contains(e.Entregado.Bultos[0].Codigo, html);
        Assert.Contains("NO-EXISTE-1", html);

        using var scope = Scope();
        Assert.Equal(EstadoEnvio.EnTransito,
            await EstadoAsync(scope.ServiceProvider.GetRequiredService<EnviosDbContext>(), e.NoIntentado.Id));
        Assert.Null((await scope.ServiceProvider.GetRequiredService<EjecucionDbContext>().Rendiciones
            .SingleAsync(r => r.RutaId == e.Ruta)).FinalizadaEn);
        Assert.Equal(EstadoRuta.EnCurso, (await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>().Rutas
            .SingleAsync(r => r.Id == e.Ruta)).Estado);
    }

    [Fact]
    public async Task El_listado_muestra_la_rendicion_pendiente_y_el_detalle_lo_declarado()
    {
        var e = await PrepararAsync();
        using var client = await OperarioAsync();

        var listado = WebUtility.HtmlDecode(await client.GetStringAsync("/backoffice/rendiciones"));
        Assert.Contains($"href=\"/backoffice/rendiciones/{e.Ruta}\"", listado);
        Assert.DoesNotContain($"<code>{e.Ruta.ToString()[^8..]}</code>", listado);
        Assert.Contains("Repartidor Demo 1", listado);
        Assert.Contains("DEMO001", listado);
        Assert.Contains("<th scope=\"col\">Chofer</th>", listado);
        Assert.Contains("<th scope=\"col\">Vehículo</th>", listado);

        var detalle = await client.GetStringAsync($"/backoffice/rendiciones/{e.Ruta}");
        Assert.All(e.NoIntentado.Bultos, b => Assert.Contains(b.Codigo, detalle));
        Assert.Contains("Fin de jornada", detalle);
        Assert.Contains("<textarea", detalle);
        Assert.Contains("handler=Comparar", detalle);
    }

    [Fact]
    public async Task La_rendicion_de_otro_operador_responde_404()
    {
        var e = await PrepararAsync();
        using var client = await OperarioAsync("deposito@envios-del-este.uy");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/backoffice/rendiciones/{e.Ruta}")).StatusCode);
    }

    [Fact]
    public async Task Solo_el_operario_de_deposito_confirma_rendiciones()
    {
        using var client = await OperarioAsync("despacho@operador-demo.uy");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/backoffice/rendiciones")).StatusCode);
    }

    [Theory]
    [InlineData("Comparar", "")]
    [InlineData("Comparar", null)]
    [InlineData("Comparar", " \r\n\t ")]
    [InlineData("Confirmar", "")]
    [InlineData("Confirmar", null)]
    [InlineData("Confirmar", " \r\n\t ")]
    public async Task Sin_bultos_recibidos_compara_y_confirma_los_faltantes_sin_error_500(string accion, string? escaneados)
    {
        var e = await PrepararAsync();
        using var client = await OperarioAsync();
        var pagina = $"/backoffice/rendiciones/{e.Ruta}";
        var campos = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await Sesion.TokenAsync(client, pagina),
        };
        if (escaneados is not null) campos["Escaneados"] = escaneados;

        var respuesta = await client.PostAsync($"{pagina}?handler={accion}", new FormUrlEncodedContent(campos));
        if (accion == "Comparar")
        {
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
            Assert.Contains("Faltan 4 bultos", html);
            Assert.DoesNotContain("The Escaneados field is required", html);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());
            Assert.Contains("Confirmar con faltantes", html);
            Assert.Contains("Faltan <strong>4 bultos</strong>", html);
            using (var antes = Scope())
            {
                Assert.Null((await antes.ServiceProvider.GetRequiredService<EjecucionDbContext>().Rendiciones
                    .SingleAsync(r => r.RutaId == e.Ruta)).FinalizadaEn);
                Assert.False(await antes.ServiceProvider.GetRequiredService<EnviosDbContext>().Incidencias
                    .AnyAsync(i => i.EnvioId == e.NoIntentado.Id || i.EnvioId == e.ConFaltante.Id));
            }
            campos["ConfirmarFaltantes"] = "true";
            respuesta = await client.PostAsync($"{pagina}?handler=Confirmar", new FormUrlEncodedContent(campos));
            Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
            Assert.Equal("/backoffice/rendiciones", respuesta.Headers.Location?.OriginalString);
        }

        using var scope = Scope();
        var rendicion = await scope.ServiceProvider.GetRequiredService<EjecucionDbContext>().Rendiciones
            .Include(r => r.Lineas).SingleAsync(r => r.RutaId == e.Ruta);
        Assert.Equal(accion == "Confirmar", rendicion.FinalizadaEn is not null);
        if (accion == "Confirmar")
        {
            Assert.Equal(4, rendicion.Lineas.Count(l => l.Recibida == false));
            var envios = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
            Assert.Equal(2, await envios.Incidencias.CountAsync(i => i.EnvioId == e.NoIntentado.Id || i.EnvioId == e.ConFaltante.Id));
            Assert.Equal(EstadoEnvio.EnTransito, await EstadoAsync(envios, e.NoIntentado.Id));
            Assert.Equal(EstadoRuta.Finalizada, (await scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>().Rutas
                .SingleAsync(r => r.Id == e.Ruta)).Estado);
        }
    }

    [Fact]
    public async Task Si_vuelven_todos_los_bultos_confirma_sin_segunda_confirmacion()
    {
        var e = await PrepararAsync();
        using var client = await OperarioAsync();
        var pagina = $"/backoffice/rendiciones/{e.Ruta}";
        var respuesta = await client.PostAsync($"{pagina}?handler=Confirmar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Escaneados"] = string.Join("\n", e.NoIntentado.Bultos.Concat(e.ConFaltante.Bultos).Select(b => b.Codigo)),
            ["__RequestVerificationToken"] = await Sesion.TokenAsync(client, pagina),
        }));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal("/backoffice/rendiciones", respuesta.Headers.Location?.OriginalString);
        using var scope = Scope();
        Assert.NotNull((await scope.ServiceProvider.GetRequiredService<EjecucionDbContext>().Rendiciones
            .SingleAsync(r => r.RutaId == e.Ruta)).FinalizadaEn);
        Assert.False(await scope.ServiceProvider.GetRequiredService<EnviosDbContext>().Incidencias
            .AnyAsync(i => i.EnvioId == e.NoIntentado.Id || i.EnvioId == e.ConFaltante.Id));
    }

    private async Task<Escenario> PrepararAsync(bool todosEntregados = false)
    {
        using var inicial = factory.CreateClient();
        await inicial.GetAsync("/health");
        using var scope = Scope();
        var ahora = DateTimeOffset.UtcNow.AddMinutes(-5);
        var fecha = new DateOnly(2024, 6, 1).AddDays(Interlocked.Increment(ref dia));

        var envios = Enumerable.Range(0, 3).Select(_ =>
        {
            var envio = Envio.Crear(DatosIniciales.OperadorDemoId, DatosIniciales.ComercioDemoId, $"CU55-{Guid.NewGuid():N}",
                new Destinatario("Prueba Rendición", "099123456"),
                new Direccion("Calle", "10", "Montevideo", "Montevideo", "11300"),
                [new DatosBulto(1, 10, 10, 10, 0), new DatosBulto(1, 10, 10, 10, 0)], OrigenEvento.PortalComercio, null,
                ahora);
            envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora);
            envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora);
            envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, ahora);
            return envio;
        }).ToArray();
        envios[2].Transicionar(EstadoEnvio.Entregado, OrigenEvento.AppRepartidor, null, ahora);
        if (todosEntregados)
            foreach (var envio in envios.Take(2)) envio.Transicionar(EstadoEnvio.Entregado, OrigenEvento.AppRepartidor, null, ahora);
        var enviosDb = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        enviosDb.Envios.AddRange(envios);
        await enviosDb.SaveChangesAsync();

        var ruta = Ruta.Crear(DatosIniciales.OperadorDemoId, fecha, DatosIniciales.RepartidorDemoId,
            Guid.Parse("51111111-1111-1111-1111-111111111111"), envios.Select(e => new EnvioRuta(e.Id, e.Numero, fecha,
                Guid.Parse("61111111-1111-1111-1111-111111111111"), null, false,
                e.Bultos.Select(b => new CargaBulto(e.Id, b.Id, b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm)).ToArray(),
                new PuntoEntrega("Calle", "10", "Montevideo", "Montevideo", "11300"))).ToArray(), ahora);
        ruta.Despachar(ahora);
        ruta.RegistrarCarga(true);
        var plan = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        plan.Rutas.Add(ruta);
        await plan.SaveChangesAsync();

        // Lo que declaró el repartidor en CU-54.
        DeclaracionRendicion[] lineas =
        [
            .. envios[0].Bultos.Select(b => new DeclaracionRendicion(envios[0].Id, b.Id, todosEntregados ? "EntregadoEnCalle" : "NoIntentado", !todosEntregados, null)),
            .. envios[1].Bultos.Select(b => new DeclaracionRendicion(envios[1].Id, b.Id, todosEntregados ? "EntregadoEnCalle" : "NoIntentado", !todosEntregados, null)),
            .. envios[2].Bultos.Select(b => new DeclaracionRendicion(envios[2].Id, b.Id, "EntregadoEnCalle", false, null)),
        ];
        var ejecucion = scope.ServiceProvider.GetRequiredService<EjecucionDbContext>();
        ejecucion.Rendiciones.Add(Rendicion.Iniciar(DatosIniciales.OperadorDemoId, ruta.Id, DatosIniciales.RepartidorDemoId,
            Guid.CreateVersion7(), ahora, "Fin de jornada", lineas));
        await ejecucion.SaveChangesAsync();

        return new Escenario(ruta.Id, envios[0], envios[1], envios[2]);
    }

    // Un ámbito con el operador de demostración como inquilino, como el de un request.
    private IServiceScope Scope()
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("operador_id", DatosIniciales.OperadorDemoId.ToString())], "Prueba")),
        };
        return scope;
    }

    private async Task<HttpClient> OperarioAsync(string email = "deposito@operador-demo.uy")
    {
        var client = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
            .CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(client, email);
        return client;
    }

    // El formulario de la página: el token antifalsificación se lee de la misma página.
    private static async Task<HttpResponseMessage> ConfirmarAsync(HttpClient client, Guid ruta, params string[] codigos)
    {
        var pagina = $"/backoffice/rendiciones/{ruta}";
        var token = await Sesion.TokenAsync(client, pagina);
        var campos = new Dictionary<string, string>
        {
            ["Escaneados"] = string.Join("\n", codigos),
            ["__RequestVerificationToken"] = token,
        };
        var respuesta = await client.PostAsync($"{pagina}?handler=Confirmar", new FormUrlEncodedContent(campos));
        if (respuesta.StatusCode == HttpStatusCode.OK &&
            (await respuesta.Content.ReadAsStringAsync()).Contains("Confirmar con faltantes"))
        {
            campos["ConfirmarFaltantes"] = "true";
            respuesta = await client.PostAsync($"{pagina}?handler=Confirmar", new FormUrlEncodedContent(campos));
        }
        return respuesta;
    }

    private static async Task<EstadoEnvio> EstadoAsync(EnviosDbContext db, Guid envioId)
    {
        return (await db.Envios.AsNoTracking().SingleAsync(e => e.Id == envioId)).Estado;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Envios;
using Logistica.Http.Contracts.Planificacion;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Npgsql;

namespace Logistica.IntegrationTests.Planificacion;

public class ArmarRutaTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly Guid Repartidor = Guid.Parse("41111111-1111-1111-1111-111111111111");
    private static readonly Guid Vehiculo = Guid.Parse("51111111-1111-1111-1111-111111111111");
    private static readonly Guid OtroRepartidor = Guid.Parse("42222222-2222-2222-2222-222222222222");
    private static readonly Guid OtroVehiculo = Guid.Parse("52222222-2222-2222-2222-222222222222");
    private HttpClient Cliente() => factory.WithWebHostBuilder(b => b.UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true")).CreateClient();

    // En UTC ya es 2 de enero, pero en Montevideo todavía es el día 1.
    private HttpClient ClienteConFechaFija() => factory.WithWebHostBuilder(b => b
        .UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true")
        .ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new RelojFijo())))
        .CreateClient();

    [Theory]
    [InlineData(2029, 12, 31, false)]
    [InlineData(2030, 1, 1, true)]
    public async Task Crear_y_prevalidar_respetan_el_dia_local_del_operador(int anio, int mes, int dia, bool valida)
    {
        using var client = ClienteConFechaFija();
        var envio = await RecibidoAsync(client);
        var fecha = new DateOnly(anio, mes, dia);
        var preview = await client.PostAsJsonAsync("/api/planificacion/rutas/prevalidacion",
            new PrevalidarRutaRequest(fecha, Repartidor, Vehiculo, [envio.Id]));
        Assert.Equal(valida ? HttpStatusCode.OK : HttpStatusCode.BadRequest, preview.StatusCode);
        var creada = await client.PostAsJsonAsync("/api/planificacion/rutas", Pedido(fecha, envio.Id));
        Assert.Equal(valida ? HttpStatusCode.Created : HttpStatusCode.BadRequest, creada.StatusCode);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/backoffice/rutas/nueva"));
        Assert.Contains("min=\"2030-01-01\"", html);
        if (valida) return;

        Assert.Contains("no puede ser anterior a hoy", await creada.Content.ReadAsStringAsync());
        await ExigirSinAsignacionAsync(envio.Id);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        var guardado = await client.PostAsync("/backoffice/rutas/nueva?handler=Confirmar", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token.Groups[1].Value,
                ["Fecha"] = "2029-12-31", ["RepartidorId"] = Repartidor.ToString(),
                ["VehiculoId"] = Vehiculo.ToString(), ["Seleccion"] = envio.Id.ToString(),
            }));
        Assert.Contains("no puede ser anterior a hoy", WebUtility.HtmlDecode(await guardado.Content.ReadAsStringAsync()));
        await ExigirSinAsignacionAsync(envio.Id);
    }

    [Fact]
    public async Task Cambiar_la_fecha_al_pasado_no_modifica_la_ruta()
    {
        using var client = ClienteConFechaFija();
        var envio = await RecibidoAsync(client);
        var ruta = await ExigirRutaAsync(await client.PostAsJsonAsync("/api/planificacion/rutas",
            Pedido(new(2030, 1, 3), envio.Id)), HttpStatusCode.Created);
        var respuesta = await client.PutAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/planificacion",
            new ModificarRutaRequest(ruta.Revision, new(2029, 12, 31), Repartidor, Vehiculo));
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var actual = (await client.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{ruta.Id}"))!;
        Assert.Equal(ruta.Fecha, actual.Fecha);
        Assert.Equal(ruta.Revision, actual.Revision);
    }

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2030, 1, 2, 1, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task Crear_asigna_y_guarda_medidas_recibidas_historial_y_outbox()
    {
        using var client = Cliente();
        var envio = await RecibidoAsync(client, peso: 2, recibido: 2.05m);
        var response = await client.PostAsJsonAsync("/api/planificacion/rutas", Pedido(new(2035, 1, 1), envio.Id));
        var ruta = await ExigirRutaAsync(response, HttpStatusCode.Created);
        Assert.Equal($"/api/planificacion/rutas/{ruta.Id}", response.Headers.Location?.ToString());
        Assert.Equal(2.05m, ruta.Carga!.PesoKg);
        Assert.Equal("AsignadoARuta", await ScalarAsync<string>("""SELECT "Estado" FROM envios."Envios" WHERE "Id"=@id""", envio.Id));
        var historial = await client.GetFromJsonAsync<List<ValidacionRutaResponse>>($"/api/planificacion/rutas/{ruta.Id}/validaciones");
        Assert.NotNull(historial);
        Assert.Single(historial);
        Assert.Equal(2.05m, Assert.Single(historial[0].Bultos).PesoKg);
        Assert.Equal(30m, historial[0].Bultos[0].LargoCm);
        Assert.Equal(1L, await ScalarAsync<long>("""SELECT count(*) FROM envios.outbox_messages WHERE "Contenido"::jsonb->>'EnvioId'=@id::text AND "Contenido"::jsonb->>'MessageId'="Id"::text""", envio.Id));
        var pagina = await client.GetAsync($"/backoffice/rutas/{ruta.Id}");
        Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
        Assert.Contains("Historial de validaciones", await pagina.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Capacidad_excedida_no_deja_ruta_asignacion_ni_outbox()
    {
        using var client = Cliente();
        var envio = await RecibidoAsync(client, peso: 1001);
        var response = await client.PostAsJsonAsync("/api/planificacion/rutas", Pedido(new(2035, 1, 2), envio.Id));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CapacidadPesoExcedida", await response.Content.ReadAsStringAsync());
        await ExigirSinAsignacionAsync(envio.Id);
    }

    [Fact]
    public async Task Agregado_revalida_toda_la_carga_y_una_modificacion_fallida_conserva_la_ruta()
    {
        using var client = Cliente();
        var a = await RecibidoAsync(client, peso: 600);
        var b = await RecibidoAsync(client, peso: 450);
        var ruta = await ExigirRutaAsync(await client.PostAsJsonAsync("/api/planificacion/rutas", Pedido(new(2035, 1, 3), a.Id)), HttpStatusCode.Created);
        var agregado = await client.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/envios", new AgregarEnviosRutaRequest(ruta.Revision, [b.Id]));
        Assert.Equal(HttpStatusCode.BadRequest, agregado.StatusCode);
        var cambio = await client.PutAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/planificacion", new ModificarRutaRequest(ruta.Revision, ruta.Fecha, OtroRepartidor, OtroVehiculo));
        Assert.Equal(HttpStatusCode.BadRequest, cambio.StatusCode);
        var actual = await client.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{ruta.Id}");
        Assert.Equal(ruta.Revision, actual!.Revision);
        Assert.Equal(Vehiculo, actual.VehiculoId);
        Assert.Single(actual.Paradas);
        await ExigirSinAsignacionAsync(b.Id);
    }

    [Fact]
    public async Task Dos_despachadores_con_el_mismo_envio_confirman_solo_una_ruta()
    {
        using var a = Cliente(); using var b = Cliente();
        var envio = await RecibidoAsync(a);
        var fecha = new DateOnly(2035, 1, 4);
        var respuestas = await EnParaleloAsync(
            () => a.PostAsJsonAsync("/api/planificacion/rutas", Pedido(fecha, envio.Id)),
            () => b.PostAsJsonAsync("/api/planificacion/rutas", new CrearRutaRequest(fecha, OtroRepartidor, OtroVehiculo, [envio.Id])));
        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1L, await ScalarAsync<long>("""SELECT count(*) FROM planificacion."Paradas" WHERE "EnvioId"=@id""", envio.Id));
        Assert.Equal(1L, await ScalarAsync<long>("""SELECT count(*) FROM envios.outbox_messages WHERE "Contenido"::jsonb->>'EnvioId'=@id::text""", envio.Id));
    }

    [Fact]
    public async Task Dos_despachadores_no_reservan_los_mismos_recursos_el_mismo_dia()
    {
        using var a = Cliente(); using var b = Cliente();
        var ea = await RecibidoAsync(a); var eb = await RecibidoAsync(b);
        var fecha = new DateOnly(2035, 1, 5);
        var respuestas = await EnParaleloAsync(
            () => a.PostAsJsonAsync("/api/planificacion/rutas", Pedido(fecha, ea.Id)),
            () => b.PostAsJsonAsync("/api/planificacion/rutas", Pedido(fecha, eb.Id)));
        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Conflict);
        var perdedor = respuestas[0].StatusCode == HttpStatusCode.Conflict ? ea : eb;
        await ExigirSinAsignacionAsync(perdedor.Id);
    }

    [Fact]
    public async Task Dos_agregados_con_igual_revision_no_sobrescriben_la_ruta()
    {
        using var a = Cliente(); using var b = Cliente();
        var inicial = await RecibidoAsync(a); var ea = await RecibidoAsync(a); var eb = await RecibidoAsync(b);
        var ruta = await ExigirRutaAsync(await a.PostAsJsonAsync("/api/planificacion/rutas", Pedido(new(2035, 1, 6), inicial.Id)), HttpStatusCode.Created);
        var respuestas = await EnParaleloAsync(
            () => a.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/envios", new AgregarEnviosRutaRequest(ruta.Revision, [ea.Id])),
            () => b.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/envios", new AgregarEnviosRutaRequest(ruta.Revision, [eb.Id])));
        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Conflict);
        var actual = await a.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{ruta.Id}");
        Assert.Equal(ruta.Revision + 1, actual!.Revision);
        Assert.Equal(2, actual.Paradas.Count);
        var historial = await a.GetFromJsonAsync<List<ValidacionRutaResponse>>($"/api/planificacion/rutas/{ruta.Id}/validaciones");
        Assert.Equal(2, historial!.Count);
        await ExigirSinAsignacionAsync(respuestas[0].StatusCode == HttpStatusCode.Conflict ? ea.Id : eb.Id);
    }

    [Fact]
    public async Task Sin_identity_el_acceso_normal_falla_cerrado()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/planificacion/recursos?fecha=2035-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/backoffice/rutas/nueva")).StatusCode);
    }

    [Fact]
    public async Task Fallo_al_guardar_planificacion_revierte_envios_eventos_y_outbox()
    {
        using var normal = Cliente();
        var envio = await RecibidoAsync(normal);
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true");
            b.ConfigureTestServices(s => s.AddScoped<IRutaRepository>(p => new RepositorioQueFalla(
                new RutaRepository(p.GetRequiredService<PlanificacionDbContext>()))));
        });
        using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/planificacion/rutas", Pedido(new(2035, 1, 7), envio.Id));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await ExigirSinAsignacionAsync(envio.Id);
        Assert.Equal(0L, await ScalarAsync<long>("""SELECT count(*) FROM envios."EventosEnvio" WHERE "EnvioId"=@id AND "EstadoNuevo"='AsignadoARuta'""", envio.Id));
    }

    [Fact]
    public async Task Ruta_en_curso_de_un_dia_anterior_bloquea_recursos_hasta_finalizar()
    {
        using var client = Cliente();
        var inicial = await RecibidoAsync(client); var otro = await RecibidoAsync(client);
        var ruta = await ExigirRutaAsync(await client.PostAsJsonAsync("/api/planificacion/rutas", Pedido(new(2035, 1, 8), inicial.Id)), HttpStatusCode.Created);
        await EjecutarSqlAsync("""UPDATE planificacion."Rutas" SET "Estado"='EnCurso' WHERE "Id"=@id""", ruta.Id);
        try
        {
            var bloqueada = await client.PostAsJsonAsync("/api/planificacion/rutas", Pedido(new(2035, 1, 9), otro.Id));
            Assert.Equal(HttpStatusCode.Conflict, bloqueada.StatusCode);
            await ExigirSinAsignacionAsync(otro.Id);
        }
        finally { await EjecutarSqlAsync("""UPDATE planificacion."Rutas" SET "Estado"='Finalizada', "ReservaActiva"=false WHERE "Id"=@id""", ruta.Id); }
        await ExigirRutaAsync(await client.PostAsJsonAsync("/api/planificacion/rutas", Pedido(new(2035, 1, 9), otro.Id)), HttpStatusCode.Created);
    }

    [Fact]
    public async Task Una_revision_vencida_devuelve_conflicto_y_no_asigna_el_envio()
    {
        using var client = Cliente();
        var inicial = await RecibidoAsync(client); var otro = await RecibidoAsync(client);
        var ruta = await ExigirRutaAsync(await client.PostAsJsonAsync("/api/planificacion/rutas", Pedido(new(2035, 1, 10), inicial.Id)), HttpStatusCode.Created);
        var actualizada = await ExigirRutaAsync(await client.PutAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/planificacion",
            new ModificarRutaRequest(ruta.Revision, ruta.Fecha, OtroRepartidor, OtroVehiculo)), HttpStatusCode.OK);
        Assert.Equal(ruta.Revision + 1, actualizada.Revision);
        var response = await client.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/envios", new AgregarEnviosRutaRequest(ruta.Revision, [otro.Id]));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("RutaModificada", await response.Content.ReadAsStringAsync());
        await ExigirSinAsignacionAsync(otro.Id);
    }

    [Fact]
    public async Task Fecha_programada_no_se_sobrescribe_y_reprogramado_se_asigna_por_T13()
    {
        using var client = Cliente();
        var envio = await RecibidoAsync(client);
        var fecha = new DateOnly(2035, 2, 1);
        await EjecutarSqlAsync("""UPDATE envios."Envios" SET "FechaEntregaProgramada"=@fecha, "Estado"='Reprogramado' WHERE "Id"=@id""", envio.Id, ("fecha", fecha));
        var erronea = await client.PostAsJsonAsync("/api/planificacion/rutas", Pedido(fecha.AddDays(1), envio.Id));
        Assert.Equal(HttpStatusCode.BadRequest, erronea.StatusCode);
        Assert.Contains("FechaIncompatible", await erronea.Content.ReadAsStringAsync());
        await ExigirRutaAsync(await client.PostAsJsonAsync("/api/planificacion/rutas", Pedido(fecha, envio.Id)), HttpStatusCode.Created);
        Assert.Equal(fecha, await ScalarAsync<DateOnly>("""SELECT "FechaEntregaProgramada" FROM envios."Envios" WHERE "Id"=@id""", envio.Id));
        Assert.Equal(1L, await ScalarAsync<long>("""SELECT count(*) FROM envios."EventosEnvio" WHERE "EnvioId"=@id AND "EstadoAnterior"='Reprogramado' AND "EstadoNuevo"='AsignadoARuta'""", envio.Id));
    }

    [Fact]
    public async Task El_indice_RF15_impide_duplicar_una_parada_activa_incluso_con_SQL_directo()
    {
        using var client = Cliente();
        var envio = await RecibidoAsync(client);
        var ruta = await ExigirRutaAsync(await client.PostAsJsonAsync("/api/planificacion/rutas", Pedido(new(2035, 2, 3), envio.Id)), HttpStatusCode.Created);
        var error = await Assert.ThrowsAsync<PostgresException>(() => EjecutarSqlAsync("""
            INSERT INTO planificacion."Paradas" ("Id", "OperadorId", "RutaId", "EnvioId", "Orden", "Estado")
            VALUES (@id, '11111111-1111-1111-1111-111111111111', @ruta, @envio, 2, 'Pendiente')
            """, Guid.NewGuid(), ("ruta", ruta.Id), ("envio", envio.Id)));
        Assert.Equal("23505", error.SqlState);
        Assert.Contains(error.ConstraintName, new[] { "UX_Paradas_EnvioActivo", "IX_Paradas_RutaId_EnvioId" });
        Assert.Equal(1L, await ScalarAsync<long>("""SELECT count(*) FROM planificacion."Paradas" WHERE "EnvioId"=@id""", envio.Id));
    }

    [Fact]
    public async Task Formulario_prevalida_sin_asignar_y_confirma_con_antiforgery()
    {
        using var client = Cliente();
        var envio = await RecibidoAsync(client);
        var otroEnvio = await RecibidoAsync(client);
        var html = await client.GetStringAsync("/backoffice/rutas/nueva");
        var validarInicial = Regex.Match(html, "<button[^>]*data-validar-ruta[^>]*>", RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.True(validarInicial.Success);
        Assert.Contains("disabled", validarInicial.Value);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.True(token.Success, html);
        var campos = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["Fecha"] = "2035-02-04", ["RepartidorId"] = Repartidor.ToString(), ["VehiculoId"] = Vehiculo.ToString(),
            ["Seleccion"] = envio.Id.ToString(), ["RevisionEsperada"] = "0", ["Pagina"] = "1",
        };
        var preview = await client.PostAsync("/backoffice/rutas/nueva?handler=Validar", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains("Confirmar ruta", await preview.Content.ReadAsStringAsync());
        var validarSeleccionado = Regex.Match(await preview.Content.ReadAsStringAsync(), "<button[^>]*data-validar-ruta[^>]*>", RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.True(validarSeleccionado.Success);
        Assert.DoesNotContain("disabled", validarSeleccionado.Value);
        await ExigirSinAsignacionAsync(envio.Id);
        var confirmado = await client.PostAsync("/backoffice/rutas/nueva?handler=Confirmar", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.OK, confirmado.StatusCode);
        Assert.Contains("Historial de validaciones", await confirmado.Content.ReadAsStringAsync());
        Assert.Contains("Ruta creada y envíos asignados correctamente.", WebUtility.HtmlDecode(await confirmado.Content.ReadAsStringAsync()));
        Assert.Equal("AsignadoARuta", await ScalarAsync<string>("""SELECT "Estado" FROM envios."Envios" WHERE "Id"=@id""", envio.Id));
        var sinToken = await client.PostAsJsonAsync("/api/planificacion/rutas", Pedido(new(2035, 2, 5), envio.Id));
        Assert.Equal(HttpStatusCode.BadRequest, sinToken.StatusCode);
        Assert.Contains("token válido", await sinToken.Content.ReadAsStringAsync());

        var rutaId = await ScalarAsync<Guid>("""SELECT "RutaId" FROM planificacion."Paradas" WHERE "EnvioId"=@id""", envio.Id);
        var detalle = $"/backoffice/rutas/{rutaId}";
        campos["Id"] = rutaId.ToString();
        campos["RevisionEsperada"] = (await client.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{rutaId}"))!.Revision.ToString();
        var datosFijos = await client.GetStringAsync(detalle);
        Assert.Contains(">Agregar envíos</button>", WebUtility.HtmlDecode(datosFijos));
        Assert.Contains("data-abrir=\"false\"", datosFijos);
        var abrirEnvios = await client.PostAsync($"{detalle}?handler=AbrirEnvios", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.OK, abrirEnvios.StatusCode);
        Assert.Contains("data-abrir=\"true\"", await abrirEnvios.Content.ReadAsStringAsync());
        var botonSinSeleccion = Regex.Match(await abrirEnvios.Content.ReadAsStringAsync(), "<button[^>]*data-validar-ruta[^>]*>", RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.True(botonSinSeleccion.Success);
        Assert.Contains("disabled", botonSinSeleccion.Value);
        campos["ModalEnvios"] = "true";
        campos["Seleccion"] = otroEnvio.Id.ToString();
        campos["Texto"] = otroEnvio.Numero;
        foreach (var accionModal in new[] { "Filtrar", "LimpiarFiltros", "Validar" })
        {
            using var solicitudModal = new HttpRequestMessage(HttpMethod.Post, $"{detalle}?handler={accionModal}")
            {
                Content = new FormUrlEncodedContent(campos)
            };
            solicitudModal.Headers.Add("X-Requested-With", "XMLHttpRequest");
            var respuestaModal = await client.SendAsync(solicitudModal);
            Assert.Equal(HttpStatusCode.OK, respuestaModal.StatusCode);
            var contenidoModal = await respuestaModal.Content.ReadAsStringAsync();
            Assert.Contains("data-contenido-envios", contenidoModal);
            Assert.DoesNotContain("<html", contenidoModal);
            Assert.DoesNotContain("modal-dialog", contenidoModal);
            Assert.Contains($"value=\"{otroEnvio.Id}\" checked", contenidoModal);
            var botonConSeleccion = Regex.Match(contenidoModal, "<button[^>]*data-validar-ruta[^>]*>", RegexOptions.None, TimeSpan.FromSeconds(1));
            Assert.True(botonConSeleccion.Success);
            Assert.DoesNotContain("disabled", botonConSeleccion.Value);
            if (accionModal == "Validar")
            {
                Assert.Contains("Confirmar agregado de envíos", WebUtility.HtmlDecode(contenidoModal));
                Assert.Contains("data-seleccion-validada=\"true\"", contenidoModal);
                Assert.Contains("data-confirmacion-validada", contenidoModal);
            }
            await ExigirSinAsignacionAsync(otroEnvio.Id);
        }
        var cerrarEnvios = await client.PostAsync($"{detalle}?handler=CerrarEnvios", new FormUrlEncodedContent(campos));
        var modalCerradoHtml = await cerrarEnvios.Content.ReadAsStringAsync();
        Assert.Contains("data-abrir=\"false\"", modalCerradoHtml);
        Assert.DoesNotContain($"value=\"{otroEnvio.Id}\" checked", modalCerradoHtml);
        await ExigirSinAsignacionAsync(otroEnvio.Id);
        campos["ModalEnvios"] = "false";
        campos.Remove("Texto");
        Assert.Contains(">Modificar</button>", datosFijos);
        Assert.DoesNotContain(">Cancelar</button>", datosFijos);
        var editar = await client.PostAsync($"{detalle}?handler=EditarDatos", new FormUrlEncodedContent(campos));
        var editarHtml = await editar.Content.ReadAsStringAsync();
        Assert.Contains("class=\"btn btn-success\"", editarHtml);
        Assert.Contains(">Cancelar</button>", editarHtml);
        Assert.DoesNotContain(">Validar selección</button>", editarHtml);
        campos["EditandoDatos"] = "true";
        campos["Fecha"] = "";
        campos["RepartidorId"] = "";
        campos["VehiculoId"] = OtroVehiculo.ToString();
        campos["Seleccion"] = otroEnvio.Id.ToString();
        var cancelado = await client.PostAsync($"{detalle}?handler=CancelarDatos", new FormUrlEncodedContent(campos));
        var canceladoHtml = await cancelado.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, cancelado.StatusCode);
        Assert.DoesNotContain("validation-summary-errors", canceladoHtml);
        Assert.DoesNotContain(">Cancelar</button>", canceladoHtml);
        Assert.Contains($"value=\"{otroEnvio.Id}\" checked", canceladoHtml);
        var sinModificar = (await client.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{rutaId}"))!;
        Assert.Equal(new DateOnly(2035, 2, 4), sinModificar.Fecha);
        Assert.Equal(Repartidor, sinModificar.RepartidorId);
        Assert.Equal(Vehiculo, sinModificar.VehiculoId);
        Assert.Equal(campos["RevisionEsperada"], sinModificar.Revision.ToString());
        await ExigirSinAsignacionAsync(otroEnvio.Id);
        campos["Fecha"] = "2035-02-04";
        campos["RepartidorId"] = Repartidor.ToString();
        campos["VehiculoId"] = Vehiculo.ToString();
        campos["EditandoDatos"] = "false";
        campos.Remove("Seleccion");
        var sinSeleccion = await client.PostAsync($"{detalle}?handler=Validar", new FormUrlEncodedContent(campos));
        var sinSeleccionHtml = WebUtility.HtmlDecode(await sinSeleccion.Content.ReadAsStringAsync());
        Assert.Contains("Seleccioná al menos un envío para validar el agregado.", sinSeleccionHtml);
        Assert.DoesNotContain("Confirmar agregado de envíos", sinSeleccionHtml);
        Assert.Contains("data-resultado-ruta=\"errores-ruta\"", sinSeleccionHtml);
        Assert.Contains("data-abrir=\"true\"", sinSeleccionHtml);

        var sinCambios = await client.PostAsync($"{detalle}?handler=Modificar", new FormUrlEncodedContent(campos));
        Assert.Contains("No hay cambios para guardar.", WebUtility.HtmlDecode(await sinCambios.Content.ReadAsStringAsync()));
        Assert.Contains(">Cancelar</button>", await sinCambios.Content.ReadAsStringAsync());
        campos["RepartidorId"] = OtroRepartidor.ToString();
        campos["VehiculoId"] = OtroVehiculo.ToString();
        var modificado = await client.PostAsync($"{detalle}?handler=Modificar", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.OK, modificado.StatusCode);
        Assert.Contains("Fecha y recursos guardados correctamente.", await modificado.Content.ReadAsStringAsync());
        Assert.DoesNotContain(">Cancelar</button>", await modificado.Content.ReadAsStringAsync());
        var ruta = (await client.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{rutaId}"))!;
        Assert.Equal(OtroRepartidor, ruta.RepartidorId);
        Assert.Equal(OtroVehiculo, ruta.VehiculoId);

        campos["RevisionEsperada"] = ruta.Revision.ToString();
        campos["Seleccion"] = otroEnvio.Id.ToString();
        var agregarPreview = await client.PostAsync($"{detalle}?handler=Validar", new FormUrlEncodedContent(campos));
        Assert.Contains("Confirmar agregado de envíos", WebUtility.HtmlDecode(await agregarPreview.Content.ReadAsStringAsync()));
        Assert.Contains("data-abrir=\"false\"", await agregarPreview.Content.ReadAsStringAsync());
        Assert.Contains("Selección validada", WebUtility.HtmlDecode(await agregarPreview.Content.ReadAsStringAsync()));
        await ExigirSinAsignacionAsync(otroEnvio.Id);
        var agregado = await client.PostAsync($"{detalle}?handler=Confirmar", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.OK, agregado.StatusCode);
        Assert.Contains("Se agregaron 1 envíos a la ruta.", WebUtility.HtmlDecode(await agregado.Content.ReadAsStringAsync()));
        Assert.Equal("AsignadoARuta", await ScalarAsync<string>("""SELECT "Estado" FROM envios."Envios" WHERE "Id"=@id""", otroEnvio.Id));
        Assert.Equal(rutaId, await ScalarAsync<Guid>("""SELECT "RutaId" FROM planificacion."Paradas" WHERE "EnvioId"=@id""", otroEnvio.Id));
    }

    [Theory]
    [InlineData("Filtrar")]
    [InlineData("LimpiarFiltros")]
    public async Task Formulario_filtra_sin_recursos_y_los_exige_al_validar_o_confirmar(string handler)
    {
        using var client = Cliente();
        var envio = await RecibidoAsync(client);
        var html = await client.GetStringAsync("/backoffice/rutas/nueva");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.True(token.Success, html);
        var campos = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["Fecha"] = "2035-02-04", ["RepartidorId"] = "", ["VehiculoId"] = "",
            ["Seleccion"] = envio.Id.ToString(), ["RevisionEsperada"] = "0", ["Pagina"] = "1",
        };
        var filtrado = await client.PostAsync($"/backoffice/rutas/nueva?handler={handler}", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.OK, filtrado.StatusCode);
        var resultado = WebUtility.HtmlDecode(await filtrado.Content.ReadAsStringAsync());
        Assert.DoesNotContain("is invalid", resultado);
        Assert.DoesNotContain("validation-summary-errors", resultado);
        Assert.Contains("value=\"2035-02-04\"", resultado);
        Assert.Contains(envio.Id.ToString(), resultado);
        foreach (var accion in new[] { "Validar", "Confirmar", "Modificar" })
        {
            var rechazado = await client.PostAsync($"/backoffice/rutas/nueva?handler={accion}", new FormUrlEncodedContent(campos));
            Assert.Equal(HttpStatusCode.OK, rechazado.StatusCode);
            var errores = WebUtility.HtmlDecode(await rechazado.Content.ReadAsStringAsync());
            Assert.Contains("Seleccioná un repartidor.", errores);
            Assert.Contains("Seleccioná un vehículo.", errores);
            Assert.DoesNotContain("Confirmar ruta", errores);
            await ExigirSinAsignacionAsync(envio.Id);
        }
    }

    [Fact]
    public async Task Disponibles_busca_por_numero_de_envio_ignorando_espacios_y_mayusculas()
    {
        using var client = Cliente();
        var envio = await RecibidoAsync(client);
        var otro = await RecibidoAsync(client);
        var texto = Uri.EscapeDataString($"  {envio.Numero.ToLowerInvariant()}  ");
        var disponibles = (await client.GetFromJsonAsync<EnviosDisponiblesResponse>(
            $"/api/planificacion/envios-disponibles?fecha=2035-04-20&texto={texto}"))!;
        Assert.Equal(1, disponibles.Total);
        Assert.Equal(envio.Id, Assert.Single(disponibles.Items).Id);
        Assert.DoesNotContain(disponibles.Items, e => e.Id == otro.Id);
    }

    [Fact]
    public async Task La_excepcion_de_desarrollo_no_habilita_acceso_en_produccion()
    {
        using var host = factory.WithWebHostBuilder(b => b.UseEnvironment("Production").UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true"));
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/planificacion/recursos?fecha=2035-01-01")).StatusCode);
    }

    private static CrearRutaRequest Pedido(DateOnly fecha, Guid envio) => new(fecha, Repartidor, Vehiculo, [envio]);
    private static async Task<RutaResponse> ExigirRutaAsync(HttpResponseMessage response, HttpStatusCode estado)
    {
        Assert.True(response.StatusCode == estado, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RutaResponse>())!;
    }
    private static async Task<HttpResponseMessage[]> EnParaleloAsync(Func<Task<HttpResponseMessage>> a, Func<Task<HttpResponseMessage>> b)
    {
        var inicio = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Ejecutar(Func<Task<HttpResponseMessage>> accion) { await inicio.Task; return await accion(); }
        var primero = Ejecutar(a); var segundo = Ejecutar(b); inicio.SetResult();
        return await Task.WhenAll(primero, segundo);
    }
    private static async Task<CrearEnvioResponse> RecibidoAsync(HttpClient client, decimal peso = 2, decimal? recibido = null)
    {
        var crear = await client.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana", "099123456", null, null),
            new DireccionRequest("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", null),
            [new BultoRequest(peso, 30, 20, 10)]));
        crear.EnsureSuccessStatusCode();
        var envio = (await crear.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
        var recepcion = await client.PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest($"{envio.Numero}-1", recibido));
        Assert.True(recepcion.IsSuccessStatusCode, await recepcion.Content.ReadAsStringAsync());
        return envio;
    }
    private async Task ExigirSinAsignacionAsync(Guid id)
    {
        Assert.Equal("EnDeposito", await ScalarAsync<string>("""SELECT "Estado" FROM envios."Envios" WHERE "Id"=@id""", id));
        Assert.Equal(0L, await ScalarAsync<long>("""SELECT count(*) FROM planificacion."Paradas" WHERE "EnvioId"=@id""", id));
        Assert.Equal(0L, await ScalarAsync<long>("""SELECT count(*) FROM envios.outbox_messages WHERE "Contenido"::jsonb->>'EnvioId'=@id::text""", id));
    }
    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var db = new NpgsqlConnection(factory.ConnectionString);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(sql, db);
        command.Parameters.AddWithValue("id", id);
        return (T)(await command.ExecuteScalarAsync())!;
    }
    private async Task EjecutarSqlAsync(string sql, Guid id, params (string Nombre, object Valor)[] parametros)
    {
        await using var db = new NpgsqlConnection(factory.ConnectionString);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(sql, db);
        command.Parameters.AddWithValue("id", id);
        foreach (var parametro in parametros) command.Parameters.AddWithValue(parametro.Nombre, parametro.Valor);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class RepositorioQueFalla(IRutaRepository original) : IRutaRepository
    {
        public Task<Ruta?> ObtenerAsync(Guid id, CancellationToken ct) => original.ObtenerAsync(id, ct);
        public Task<IReadOnlyList<Ruta>> ListarAsync(DateOnly? fecha, CancellationToken ct) => original.ListarAsync(fecha, ct);
        public Task<IReadOnlyList<Guid>> EnviosOcupadosAsync(IReadOnlyCollection<Guid> ids, Guid? propia, CancellationToken ct) => original.EnviosOcupadosAsync(ids, propia, ct);
        public Task<IReadOnlyList<string>> RecursosOcupadosAsync(DateOnly fecha, Guid repartidor, Guid vehiculo, Guid? propia, CancellationToken ct) => original.RecursosOcupadosAsync(fecha, repartidor, vehiculo, propia, ct);
        public Task<IReadOnlyList<ValidacionRuta>> ValidacionesAsync(Guid id, int pagina, CancellationToken ct) => original.ValidacionesAsync(id, pagina, ct);
        public void Agregar(Ruta ruta) => original.Agregar(ruta);
        public void Agregar(ValidacionRuta validacion) => original.Agregar(validacion);
        public Task GuardarAsync(CancellationToken ct) => throw new InvalidOperationException("Fallo inyectado después de guardar Envíos.");
    }
}

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
        var html = await client.GetStringAsync("/backoffice/rutas/nueva");
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
        await ExigirSinAsignacionAsync(envio.Id);
        var confirmado = await client.PostAsync("/backoffice/rutas/nueva?handler=Confirmar", new FormUrlEncodedContent(campos));
        Assert.Equal(HttpStatusCode.OK, confirmado.StatusCode);
        Assert.Contains("Historial de validaciones", await confirmado.Content.ReadAsStringAsync());
        Assert.Equal("AsignadoARuta", await ScalarAsync<string>("""SELECT "Estado" FROM envios."Envios" WHERE "Id"=@id""", envio.Id));
        var sinToken = await client.PostAsJsonAsync("/api/planificacion/rutas", Pedido(new(2035, 2, 5), envio.Id));
        Assert.Equal(HttpStatusCode.BadRequest, sinToken.StatusCode);
        Assert.Contains("token válido", await sinToken.Content.ReadAsStringAsync());
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

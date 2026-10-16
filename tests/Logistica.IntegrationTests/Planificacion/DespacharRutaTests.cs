using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Envios;
using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Administracion.Domain.Operadores;
using Logistica.Modules.Administracion.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using Logistica.Modules.Planificacion.Application.Abstractions;
using Logistica.SharedKernel;

namespace Logistica.IntegrationTests.Planificacion;

public class DespacharRutaTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static int _dia;
    private HttpClient Cliente() => factory.WithWebHostBuilder(b =>
        b.UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true")).CreateClient();

    [Fact]
    public async Task Confirmacion_despacha_con_evidencia_outbox_y_bloquea_ediciones()
    {
        using var client = Cliente();
        var ruta = await CrearAsync(client);
        var url = $"/backoffice/rutas/{ruta.Id}";
        var html = await client.GetStringAsync(url);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", WebUtility.HtmlDecode(token.Groups[1].Value));
        var formulario = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["RevisionEsperada"] = ruta.Revision.ToString(), ["Fecha"] = ruta.Fecha.ToString("yyyy-MM-dd")
        };
        var preview = await client.PostAsync(url + "?handler=PrepararDespacho", new FormUrlEncodedContent(formulario));
        Assert.Contains("Confirmar despacho", await preview.Content.ReadAsStringAsync());
        Assert.Equal("Planificada", (await client.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{ruta.Id}"))!.Estado);
        var respuesta = await client.PostAsync(url + "?handler=Despachar", new FormUrlEncodedContent(formulario));
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var actual = (await client.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{ruta.Id}"))!;
        Assert.Equal("Despachada", actual.Estado);
        Assert.Equal(ruta.Revision + 1, actual.Revision);
        Assert.DoesNotContain("Validar despacho", await client.GetStringAsync(url));
        Assert.Equal(HttpStatusCode.Conflict, (await DespacharAsync(client, actual)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await QuitarAsync(client, actual)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        var entidad = await db.Rutas.SingleAsync(r => r.Id == ruta.Id);
        Assert.NotNull(entidad.DespachadaEn);
        Assert.Single(await db.Validaciones.Where(v => v.RutaId == ruta.Id && v.Operacion == Logistica.Modules.Planificacion.Domain.Rutas.OperacionValidacionRuta.Despacho).ToListAsync());
        var mensajes = await db.Outbox.Where(m => m.Tipo == "RutaDespachada.v1").ToListAsync();
        var mensaje = Assert.Single(mensajes, m =>
            JsonDocument.Parse(m.Contenido).RootElement.GetProperty("RutaId").GetGuid() == ruta.Id);
        using var contenido = JsonDocument.Parse(mensaje.Contenido);
        var evento = contenido.RootElement;
        Assert.Equal(ruta.Id, evento.GetProperty("RutaId").GetGuid());
        Assert.Equal(ruta.RepartidorId, evento.GetProperty("RepartidorId").GetGuid());
        Assert.Equal(actual.Revision, evento.GetProperty("Revision").GetInt64());
        Assert.Equal(entidad.OperadorId, mensaje.OperadorId);
        Assert.Null(mensaje.PublicadoEn);
        entidad.Finalizar();
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Repartidor_sin_usuario_puede_planificarse_pero_no_prevalidar_ni_despachar()
    {
        using var client = Cliente();
        var ruta = await CrearAsync(client, Guid.Parse("42222222-2222-2222-2222-222222222222"));
        var prevalidacion = await client.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/despacho/prevalidacion",
            new DespacharRutaRequest(ruta.Revision));
        Assert.Equal(HttpStatusCode.BadRequest, prevalidacion.StatusCode);
        Assert.Contains("usuario vinculado", await prevalidacion.Content.ReadAsStringAsync());
        var rechazo = await DespacharAsync(client, ruta);
        Assert.Equal(HttpStatusCode.BadRequest, rechazo.StatusCode);
        Assert.Contains("usuario vinculado", await rechazo.Content.ReadAsStringAsync());

        var url = $"/backoffice/rutas/{ruta.Id}";
        var html = await client.GetStringAsync(url);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        var formulario = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["RevisionEsperada"] = ruta.Revision.ToString(), ["Fecha"] = ruta.Fecha.ToString("yyyy-MM-dd")
        };
        foreach (var handler in new[] { "PrepararDespacho", "Despachar" })
        {
            var pantalla = await client.PostAsync(url + "?handler=" + handler, new FormUrlEncodedContent(formulario));
            pantalla.EnsureSuccessStatusCode();
            var mensaje = WebUtility.HtmlDecode(await pantalla.Content.ReadAsStringAsync());
            Assert.Contains("Repartidor Demo 2 no tiene un usuario vinculado", mensaje);
            Assert.DoesNotContain("Confirmar despacho", mensaje);
        }
        await ExigirSinDespachoAsync(ruta);
    }

    [Fact]
    public async Task Recomprueba_el_usuario_al_confirmar_aunque_la_prevalidacion_haya_sido_valida()
    {
        using var client = Cliente();
        var ruta = await CrearAsync(client);
        (await client.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/despacho/prevalidacion",
            new DespacharRutaRequest(ruta.Revision))).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<AdministracionDbContext>();
        var repartidor = await admin.Repartidores.SingleAsync(r => r.Id == ruta.RepartidorId);
        var usuario = repartidor.UsuarioId;
        Assert.NotNull(usuario);
        admin.Entry(repartidor).Property(r => r.UsuarioId).CurrentValue = null;
        await admin.SaveChangesAsync();
        try
        {
            var rechazo = await DespacharAsync(client, ruta);
            Assert.Equal(HttpStatusCode.BadRequest, rechazo.StatusCode);
            Assert.Contains("usuario vinculado", await rechazo.Content.ReadAsStringAsync());
            await ExigirSinDespachoAsync(ruta);
        }
        finally
        {
            admin.Entry(repartidor).Property(r => r.UsuarioId).CurrentValue = usuario;
            await admin.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData("Pendiente", false)]
    [InlineData("Pendiente", true)]
    [InlineData("Desactivado", false)]
    [InlineData("Desactivado", true)]
    [InlineData("OtroPerfil", false)]
    [InlineData("OtroPerfil", true)]
    [InlineData("OtroOperador", false)]
    [InlineData("OtroOperador", true)]
    [InlineData("Inexistente", false)]
    [InlineData("Inexistente", true)]
    public async Task Cuenta_inhabilitada_impide_despachar_incluso_despues_de_prevalidar(string motivo, bool prevalidarAntes)
    {
        using var client = Cliente();
        using var scope = factory.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<AdministracionDbContext>();
        var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var operador = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var repartidor = await admin.Repartidores.SingleAsync(r => r.Id == Guid.Parse("42222222-2222-2222-2222-222222222222"));
        var anterior = repartidor.UsuarioId;
        var usuario = Usuario.CrearPersonal(operador, "Repartidor CU43", $"cu43-{Guid.NewGuid():N}@prueba.uy");
        Assert.True((await usuarios.CreateAsync(usuario, UsuariosIniciales.ContrasenaDemo)).Succeeded);
        Assert.True((await usuarios.AddToRoleAsync(usuario, "Repartidor")).Succeeded);
        admin.Entry(repartidor).Property(r => r.UsuarioId).CurrentValue = usuario.Id;
        await admin.SaveChangesAsync();
        try
        {
            var ruta = await CrearAsync(client, repartidor.Id);
            if (prevalidarAntes)
                (await client.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/despacho/prevalidacion",
                    new DespacharRutaRequest(ruta.Revision))).EnsureSuccessStatusCode();
            switch (motivo)
            {
                case "Pendiente":
                    usuario.PasswordHash = null;
                    Assert.True((await usuarios.UpdateAsync(usuario)).Succeeded);
                    break;
                case "Desactivado":
                    usuario.Desactivar();
                    Assert.True((await usuarios.UpdateAsync(usuario)).Succeeded);
                    break;
                case "OtroPerfil":
                    Assert.True((await usuarios.RemoveFromRoleAsync(usuario, "Repartidor")).Succeeded);
                    Assert.True((await usuarios.AddToRoleAsync(usuario, "Despachador")).Succeeded);
                    break;
                case "OtroOperador":
                    var ajeno = Usuario.CrearPersonal(Guid.Parse("33333333-3333-3333-3333-333333333333"),
                        "Repartidor ajeno CU43", $"cu43-ajeno-{Guid.NewGuid():N}@prueba.uy");
                    Assert.True((await usuarios.CreateAsync(ajeno, UsuariosIniciales.ContrasenaDemo)).Succeeded);
                    Assert.True((await usuarios.AddToRoleAsync(ajeno, "Repartidor")).Succeeded);
                    admin.Entry(repartidor).Property(r => r.UsuarioId).CurrentValue = ajeno.Id;
                    await admin.SaveChangesAsync();
                    break;
                case "Inexistente":
                    admin.Entry(repartidor).Property(r => r.UsuarioId).CurrentValue = Guid.NewGuid();
                    await admin.SaveChangesAsync();
                    break;
            }
            var prevalidacion = await client.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/despacho/prevalidacion",
                new DespacharRutaRequest(ruta.Revision));
            Assert.Equal(HttpStatusCode.BadRequest, prevalidacion.StatusCode);
            Assert.Contains("debe estar activada, activa", await prevalidacion.Content.ReadAsStringAsync());
            var rechazo = await DespacharAsync(client, ruta);
            Assert.Equal(HttpStatusCode.BadRequest, rechazo.StatusCode);
            Assert.Contains("debe estar activada, activa", await rechazo.Content.ReadAsStringAsync());
            await ExigirSinDespachoAsync(ruta);
        }
        finally
        {
            admin.Entry(repartidor).Property(r => r.UsuarioId).CurrentValue = anterior;
            await admin.SaveChangesAsync();
        }
    }

    private async Task ExigirSinDespachoAsync(RutaResponse ruta)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        var actual = await db.Rutas.AsNoTracking().SingleAsync(r => r.Id == ruta.Id);
        Assert.Equal(Logistica.Modules.Planificacion.Domain.Rutas.EstadoRuta.Planificada, actual.Estado);
        Assert.Equal(ruta.Revision, actual.Revision);
        Assert.Null(actual.DespachadaEn);
        Assert.False(await db.Validaciones.AnyAsync(v => v.RutaId == ruta.Id
            && v.Operacion == Logistica.Modules.Planificacion.Domain.Rutas.OperacionValidacionRuta.Despacho));
        Assert.DoesNotContain(await db.Outbox.Where(m => m.Tipo == "RutaDespachada.v1").ToListAsync(),
            m => JsonDocument.Parse(m.Contenido).RootElement.GetProperty("RutaId").GetGuid() == ruta.Id);
    }

    [Fact]
    public async Task Quitar_una_parada_invalida_la_confirmacion_y_ruta_vacia_no_despacha()
    {
        using var client = Cliente();
        var ruta = await CrearAsync(client);
        var quitar = await QuitarAsync(client, ruta);
        quitar.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await DespacharAsync(client, ruta)).StatusCode);
        var vacia = (await quitar.Content.ReadFromJsonAsync<RutaResponse>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await DespacharAsync(client, vacia)).StatusCode);
        Assert.DoesNotContain("Validar despacho", await client.GetStringAsync($"/backoffice/rutas/{ruta.Id}"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        Assert.DoesNotContain(await db.Outbox.ToListAsync(), m => JsonDocument.Parse(m.Contenido).RootElement.GetProperty("RutaId").GetGuid() == ruta.Id);
        Assert.Equal("Planificada", (await client.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{ruta.Id}"))!.Estado);
    }

    [Fact]
    public async Task Revalida_el_vehiculo_al_confirmar_y_no_guarda_evento_si_se_desactivo()
    {
        using var client = Cliente();
        var ruta = await CrearAsync(client);
        (await client.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/despacho/prevalidacion",
            new DespacharRutaRequest(ruta.Revision))).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<AdministracionDbContext>();
        var vehiculo = await admin.Vehiculos.SingleAsync(v => v.Id == ruta.VehiculoId);
        admin.Entry(vehiculo).Property(v => v.Activo).CurrentValue = false;
        await admin.SaveChangesAsync();
        try
        {
            var rechazo = await DespacharAsync(client, ruta);
            Assert.Equal(HttpStatusCode.BadRequest, rechazo.StatusCode);
            Assert.Contains("inactivo", await rechazo.Content.ReadAsStringAsync());
            var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
            var entidad = await db.Rutas.SingleAsync(r => r.Id == ruta.Id);
            Assert.Null(entidad.DespachadaEn);
            Assert.Equal(ruta.Revision, entidad.Revision);
            Assert.DoesNotContain(await db.Outbox.ToListAsync(), m =>
                JsonDocument.Parse(m.Contenido).RootElement.GetProperty("RutaId").GetGuid() == ruta.Id);
        }
        finally
        {
            admin.Entry(vehiculo).Property(v => v.Activo).CurrentValue = true;
            await admin.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Despachar_y_quitar_con_la_misma_revision_solo_aplican_una_operacion()
    {
        using var client = Cliente(); using var otro = Cliente();
        var ruta = await CrearAsync(client);
        var resultados = await Task.WhenAll(DespacharAsync(client, ruta), QuitarAsync(otro, ruta));
        Assert.Single(resultados, r => r.IsSuccessStatusCode);
        Assert.Single(resultados, r => r.StatusCode == HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        var actual = await db.Rutas.Include(r => r.Paradas).ThenInclude(p => p.Envios).SingleAsync(r => r.Id == ruta.Id);
        Assert.Equal(ruta.Revision + 1, actual.Revision);
        if (actual.DespachadaEn is not null)
        {
            Assert.Single(actual.Paradas);
            actual.Finalizar();
            await db.SaveChangesAsync();
        }
        else Assert.Empty(actual.Paradas);
    }

    [Fact]
    public async Task Fallo_antes_del_commit_revierte_estado_evidencia_y_evento()
    {
        using var client = Cliente();
        var ruta = await CrearAsync(client);
        using var fallido = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IConfirmacionPlanificacion>();
                s.AddScoped<IConfirmacionPlanificacion, ConfirmacionQueFalla>();
            });
        }).CreateClient();
        Assert.Equal(HttpStatusCode.InternalServerError, (await DespacharAsync(fallido, ruta)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        var actual = await db.Rutas.SingleAsync(r => r.Id == ruta.Id);
        Assert.Equal(ruta.Revision, actual.Revision);
        Assert.Null(actual.DespachadaEn);
        Assert.DoesNotContain(await db.Validaciones.Where(v => v.RutaId == ruta.Id).ToListAsync(),
            v => v.Operacion == Logistica.Modules.Planificacion.Domain.Rutas.OperacionValidacionRuta.Despacho);
        Assert.DoesNotContain(await db.Outbox.ToListAsync(), m =>
            JsonDocument.Parse(m.Contenido).RootElement.GetProperty("RutaId").GetGuid() == ruta.Id);
    }
    [Fact]
    public async Task Despacho_exige_sesion_y_no_permite_rutas_de_otro_operador()
    {
        using var client = Cliente();
        var ruta = await CrearAsync(client);
        using var anonimo = factory.WithWebHostBuilder(b =>
            b.UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "false")).CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await DespacharAsync(anonimo, ruta)).StatusCode);
        var otroOperador = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<AdministracionDbContext>();
            admin.Operadores.Add(Operador.Crear("Otro operador CU43", "cu43-" + otroOperador.ToString("N"),
                "America/Montevideo", otroOperador));
            await admin.SaveChangesAsync();
        }
        using var ajeno = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true");
            b.UseSetting("Database:MigrateOnStartup", "false");
            b.UseSetting("TenantProvisorio:OperadorId", otroOperador.ToString());
        }).CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await DespacharAsync(ajeno, ruta)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ajeno.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/despacho/prevalidacion",
            new DespacharRutaRequest(ruta.Revision))).StatusCode);
        Assert.Equal("Planificada", (await client.GetFromJsonAsync<RutaResponse>($"/api/planificacion/rutas/{ruta.Id}"))!.Estado);
    }

    private sealed class ConfirmacionQueFalla(IUnidadDeTrabajo unidad) : IConfirmacionPlanificacion
    {
        public Task EjecutarAsync(Func<Task> trabajo, IReadOnlyCollection<Guid> ids, CancellationToken ct)
            => unidad.EjecutarAsync(async () =>
            {
                await trabajo();
                throw new InvalidOperationException("Fallo simulado después de SaveChanges y antes del commit.");
            }, ct);
    }

    private static Task<HttpResponseMessage> DespacharAsync(HttpClient client, RutaResponse ruta)
        => client.PostAsJsonAsync($"/api/planificacion/rutas/{ruta.Id}/despacho", new DespacharRutaRequest(ruta.Revision));
    private static Task<HttpResponseMessage> QuitarAsync(HttpClient client, RutaResponse ruta)
        => client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,
            $"/api/planificacion/rutas/{ruta.Id}/envios/{ruta.Paradas[0].Envios[0].EnvioId}")
            { Content = JsonContent.Create(new QuitarEnvioDeRutaRequest(ruta.Revision)) });
    private static async Task<RutaResponse> CrearAsync(HttpClient client, Guid? repartidorId = null)
    {
        var creado = await client.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana CU43", "099123456", null, null),
            new DireccionRequest("Av. Italia", Guid.NewGuid().ToString("N"), "Montevideo", "Montevideo", "11300", null),
            [new BultoRequest(2, 30, 20, 10)]));
        creado.EnsureSuccessStatusCode();
        var envio = (await creado.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
        (await client.PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest(envio.Numero + "-1", null))).EnsureSuccessStatusCode();
        var fecha = new DateOnly(2039, 1, 1).AddDays(Interlocked.Increment(ref _dia));
        var response = await client.PostAsJsonAsync("/api/planificacion/rutas", new CrearRutaRequest(fecha,
            repartidorId ?? Guid.Parse("41111111-1111-1111-1111-111111111111"), Guid.Parse("51111111-1111-1111-1111-111111111111"), [envio.Id]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RutaResponse>())!;
    }
}

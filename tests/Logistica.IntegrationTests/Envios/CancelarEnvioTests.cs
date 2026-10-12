using System.Net;
using System.Net.Http.Json;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Logistica.IntegrationTests.Envios;

// CU-16 de punta a punta: el comercio cancela un envío Admitido (T17) y queda EnvioCancelado en el Outbox.
// Las rutas /api usan el operador y el comercio del inquilino provisorio (TenantProvisorio).
public class CancelarEnvioTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly Guid OperadorProvisorio = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Cancelar_un_envio_admitido_responde_204_y_lo_deja_Cancelado_con_su_evento_y_su_mensaje()
    {
        var client = factory.CreateClient();
        var envio = await CrearAsync(client);

        var response = await client.PostAsJsonAsync($"/api/envios/{envio.Id}/cancelacion",
            new CancelarEnvioRequest("El cliente se arrepintió"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Cancelado", await EstadoAsync(envio.Id));
        Assert.Equal("El cliente se arrepintió", await ConsultarAsync<string>(
            """SELECT "Detalle" FROM envios."EventosEnvio" WHERE "EnvioId" = @id AND "EstadoNuevo" = 'Cancelado'""", envio.Id));
        Assert.Equal(1L, await MensajesCanceladoAsync(envio.Id));

        // El inquilino del sobre sale de la sesión: el comercio provisorio que canceló (ADR-0002, sección 2.5).
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), await ConsultarAsync<Guid>(
            """SELECT "ComercioId" FROM envios.outbox_messages WHERE "Tipo" = 'EnvioCancelado.v1' AND "Contenido"::jsonb ->> 'EnvioId' = @id::text""",
            envio.Id));

    }

    [Fact]
    public async Task Se_puede_cancelar_sin_motivo()
    {
        var client = factory.CreateClient();
        var envio = await CrearAsync(client);

        var response = await client.PostAsync($"/api/envios/{envio.Id}/cancelacion", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Cancelado", await EstadoAsync(envio.Id));
    }

    // A1: mientras el comercio confirmaba, el operario lo recibió en depósito.
    [Fact]
    public async Task Un_envio_ya_recibido_en_deposito_no_se_puede_cancelar()
    {
        var client = factory.CreateClient();
        var envio = await CrearAsync(client);
        await RecibirEnDepositoAsync(envio.Id);

        var response = await client.PostAsJsonAsync($"/api/envios/{envio.Id}/cancelacion", new CancelarEnvioRequest(null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problema = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("El envío no puede pasar de EnDeposito a Cancelado.", problema?.Detail);
        Assert.Equal("EnDeposito", await EstadoAsync(envio.Id));
        Assert.Equal(0L, await MensajesCanceladoAsync(envio.Id));
    }

    // Segundo nivel de aislamiento (ADR-0002, sección 2.6): para el comercio, el envío de otro comercio no existe.
    [Fact]
    public async Task Un_envio_de_otro_comercio_del_mismo_operador_responde_404_y_no_cambia()
    {
        var client = factory.CreateClient();
        var ajeno = await GuardarEnvioDeOtroComercioAsync();

        var response = await client.PostAsJsonAsync($"/api/envios/{ajeno.Id}/cancelacion", new CancelarEnvioRequest(null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Admitido", await EstadoAsync(ajeno.Id));
    }

    [Fact]
    public async Task Un_envio_inexistente_responde_404()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/envios/{Guid.NewGuid()}/cancelacion", new CancelarEnvioRequest(null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Lo crea por la API, como el comercio del inquilino provisorio.
    private static async Task<CrearEnvioResponse> CrearAsync(HttpClient client)
    {
        var creado = await client.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana Pérez", "099123456", null, null),
            new DireccionRequest("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", null),
            [new BultoRequest(2, 30, 20, 10)]));
        creado.EnsureSuccessStatusCode();
        return (await creado.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
    }

    // T2 como personal del operador: fuera de un request, TenantProvisorio no tiene comercio (como en RecepcionDesdeDetalleTests).
    private async Task RecibirEnDepositoAsync(Guid envioId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var envio = await db.Envios.FindAsync(envioId);
        envio!.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    // Un envío del mismo operador pero de otro comercio, guardado con su propio inquilino.
    private async Task<Envio> GuardarEnvioDeOtroComercioAsync()
    {
        var ajeno = Envio.Crear(OperadorProvisorio, Guid.NewGuid(), $"ENV-AJENO-{Guid.NewGuid():N}",
            new Destinatario("Ajeno", "099000000"), new Direccion("Calle", "1", "Montevideo", "Montevideo", "11200"),
            [new DatosBulto(2, 30, 20, 10, 200)], OrigenEvento.PortalComercio, null, DateTimeOffset.UtcNow);

        using var scope = factory.Services.CreateScope();
        await using var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider,
            new InquilinoFijo(ajeno.OperadorId, ajeno.ComercioId));
        db.Envios.Add(ajeno);
        await db.SaveChangesAsync();
        return ajeno;
    }

    private Task<string> EstadoAsync(Guid envioId) =>
        ConsultarAsync<string>("""SELECT "Estado" FROM envios."Envios" WHERE "Id" = @id""", envioId);

    private Task<long> MensajesCanceladoAsync(Guid envioId) =>
        ConsultarAsync<long>(
            """SELECT count(*) FROM envios.outbox_messages WHERE "Tipo" = 'EnvioCancelado.v1' AND "Contenido"::jsonb ->> 'EnvioId' = @id::text""",
            envioId);

    // Lee directo de la base, sin pasar por la API, para comprobar lo que quedó guardado.
    private async Task<T> ConsultarAsync<T>(string sql, Guid envioId)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("id", envioId);

        return (T)(await comando.ExecuteScalarAsync())!;
    }
}

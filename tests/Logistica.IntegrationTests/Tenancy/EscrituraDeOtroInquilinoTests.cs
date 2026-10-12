using System.Net;
using System.Net.Http.Json;
using Logistica.Http.Contracts.Deposito;
using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Deposito.Domain.Recepciones;
using Logistica.Modules.Deposito.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Logistica.IntegrationTests.Tenancy;

// La respuesta HTTP cuando el interceptor rechaza una escritura de otro inquilino (ADR-0002, sección 2.9):
// el mismo 404 sin cuerpo que un recurso inexistente, y no queda nada guardado.
public sealed class EscrituraDeOtroInquilinoTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task Una_escritura_rechazada_por_el_interceptor_responde_404_como_un_recurso_inexistente_y_no_guarda_nada()
    {
        var envio = await CrearEnvioAsync(factory.CreateClient());

        // El repositorio cambia la recepción por una de otro operador antes de guardarla. No hay un camino real
        // para hacerlo (el filtro no deja leer lo ajeno), así que se fuerza; el interceptor que la rechaza es el real.
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddScoped<IRecepcionRepository>(p => new RepositorioDeOtroOperador(
                new RecepcionRepository(p.GetRequiredService<DepositoDbContext>())))));
        var client = host.CreateClient();

        var rechazada = await client.PostAsJsonAsync("/api/deposito/recepciones", new RecibirBultoRequest($"{envio.Numero}-1"));
        var inexistente = await client.GetAsync($"/backoffice/envios/INEXISTENTE-{Guid.NewGuid():N}");

        // Mismo código y mismo cuerpo vacío: la respuesta no confirma que el recurso exista.
        Assert.Equal(HttpStatusCode.NotFound, rechazada.StatusCode);
        Assert.Equal(inexistente.StatusCode, rechazada.StatusCode);
        Assert.Empty(await rechazada.Content.ReadAsByteArrayAsync());
        Assert.Empty(await inexistente.Content.ReadAsByteArrayAsync());

        // La recepción y la transición T2 iban en la misma transacción: no quedó ninguna de las dos.
        Assert.Equal(0L, await EscalarAsync<long>("""SELECT count(*) FROM deposito."Recepciones" WHERE "EnvioId" = @id""", envio.Id));
        Assert.Equal("Admitido", await EscalarAsync<string>("""SELECT "Estado" FROM envios."Envios" WHERE "Id" = @id""", envio.Id));
    }

    private static async Task<CrearEnvioResponse> CrearEnvioAsync(HttpClient client)
    {
        var request = new CrearEnvioRequest(
            new DestinatarioRequest("Ana Pérez", "099123456", Email: null, Documento: null),
            new DireccionRequest("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", Referencia: null),
            [new BultoRequest(PesoKg: 2, LargoCm: 30, AnchoCm: 20, AltoCm: 10)]);

        var response = await client.PostAsJsonAsync("/api/envios", request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CrearEnvioResponse>())!;
    }

    // Lee directo de la base, sin pasar por la API.
    private async Task<T> EscalarAsync<T>(string sql, Guid envioId)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("id", envioId);

        return (T)(await comando.ExecuteScalarAsync())!;
    }

    // Entrega al repositorio real una recepción de otro operador en lugar de la recibida.
    private sealed class RepositorioDeOtroOperador(IRecepcionRepository original) : IRecepcionRepository
    {
        public Task<IReadOnlyList<Guid>> BultosRecibidosAsync(Guid envioId, CancellationToken ct)
            => original.BultosRecibidosAsync(envioId, ct);

        public Task<IReadOnlyList<RecepcionDeposito>> ConsultarPorBultosAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct)
            => original.ConsultarPorBultosAsync(bultoIds, ct);

        public void Agregar(RecepcionDeposito recepcion)
        {
            var medidas = new Medidas(1, 1, 1, 1);
            original.Agregar(RecepcionDeposito.Registrar(
                Guid.NewGuid(), recepcion.EnvioId, recepcion.BultoId, medidas, medidas, recepcion.RecibidoEn));
        }

        public Task GuardarCambiosAsync(CancellationToken ct) => original.GuardarCambiosAsync(ct);
    }
}

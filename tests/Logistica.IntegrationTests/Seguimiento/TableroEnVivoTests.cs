using System.Net;
using Logistica.Http.Contracts.Seguimiento;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Ejecucion.Contracts.Events;
using Logistica.Modules.Ejecucion.Contracts.Results;
using Logistica.Modules.Seguimiento.Application.Features.VerTablero;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Seguimiento;

// CU-66, requerimiento especial 1, con un cliente SignalR de verdad: cada conexión entra a la sala de su operador
// (ADR-0002, sección 2.5). El despachador de Demo recibe lo de Demo y el de Este nada; sin el perfil, no se conecta.
public class TableroEnVivoTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(5);

    private WebApplicationFactory<Program> Api() => factory
        .WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"));

    [Fact]
    public async Task Una_novedad_de_demo_llega_solo_al_despachador_de_demo()
    {
        var api = Api();
        await using var demo = await ConectarAsync(api, "despacho@operador-demo.uy");
        await using var este = await ConectarAsync(api, "despacho@envios-del-este.uy");
        var paraDemo = Recibir<NovedadTableroResponse>(demo, "novedad");
        var paraEste = Recibir<NovedadTableroResponse>(este, "novedad");

        using (var scope = api.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITableroEnVivo>().NovedadAsync(DatosIniciales.OperadorDemoId,
                new NovedadTableroResponse("Prueba", "ENV-1 se entregó.", false, DateTimeOffset.UtcNow), CancellationToken.None);
        }

        Assert.Equal("ENV-1 se entregó.", (await paraDemo.WaitAsync(Espera)).Texto);
        await Task.Delay(500);
        Assert.False(paraEste.IsCompleted);
    }

    // CU-53 → CU-66: la posición reportada en la API llega al tablero de su operador por el consumidor en memoria.
    [Fact]
    public async Task Una_posicion_reportada_llega_al_tablero_de_su_operador()
    {
        var api = Api();
        await using var demo = await ConectarAsync(api, "despacho@operador-demo.uy");
        var recibida = Recibir<PosicionTableroResponse>(demo, "posicion");
        var posicion = new PosicionVehiculoResult(Guid.NewGuid(), DatosIniciales.OperadorDemoId, Guid.NewGuid(), Guid.NewGuid(),
            -34.9m, -56.16m, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        using (var scope = api.Services.CreateScope())
        {
            foreach (var consumidor in scope.ServiceProvider.GetServices<IConsumidorPosicionReportada>())
            {
                await consumidor.ConsumirAsync(new PosicionReportada(posicion, SoloHistorial: false), CancellationToken.None);
            }
        }

        var llegada = await recibida.WaitAsync(Espera);
        Assert.Equal(posicion.Id, llegada.Id);
        Assert.Equal(posicion.VehiculoId, llegada.VehiculoId);
    }

    // Pre-condición 1: el operario de depósito y quien no inició sesión no se conectan al hub.
    [Fact]
    public async Task Sin_el_perfil_no_se_conecta_al_hub()
    {
        var api = Api();

        await Assert.ThrowsAnyAsync<Exception>(() => ConectarAsync(api, "deposito@operador-demo.uy"));
        await Assert.ThrowsAnyAsync<Exception>(() => ConectarAsync(api, email: null));
    }

    // Un TaskCompletionSource que se completa con el primer mensaje de ese tipo.
    private static Task<T> Recibir<T>(HubConnection conexion, string metodo)
    {
        var llegada = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        conexion.On<T>(metodo, mensaje => llegada.TrySetResult(mensaje));
        return llegada.Task;
    }

    // Inicia sesión con la cookie y se conecta al hub con esa misma cookie, por long polling sobre el TestServer.
    private static async Task<HubConnection> ConectarAsync(WebApplicationFactory<Program> api, string? email)
    {
        var cookies = new CookieContainer();
        HttpMessageHandler Manejador() => new CookieContainerHandler(cookies) { InnerHandler = api.Server.CreateHandler() };

        if (email is not null)
        {
            using var navegador = new HttpClient(Manejador()) { BaseAddress = new Uri("https://localhost") };
            await Sesion.IngresarAsync(navegador, email);
        }

        var conexion = new HubConnectionBuilder()
            .WithUrl("https://localhost/hubs/tablero", opciones =>
            {
                opciones.HttpMessageHandlerFactory = _ => Manejador();
                opciones.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        await conexion.StartAsync();
        return conexion;
    }
}

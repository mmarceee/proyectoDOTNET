using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Http.Contracts.Envios;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento;
using Logistica.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Logistica.IntegrationTests.Resiliencia;

// Pruebas de resiliencia (opcional 7.4, docs/pruebas-de-resiliencia.md): cada una detiene de verdad el contenedor de una
// dependencia con la API corriendo, comprueba el comportamiento degradado y lo vuelve a levantar para comprobar que el
// sistema se recupera solo, sin reiniciar la API ni el Worker.
//
// Testcontainers publica cada puerto en uno libre del host, pero al detener y volver a iniciar el contenedor el puerto
// puede cambiar, y la API ya tiene la dirección vieja. Por eso estos contenedores usan un puerto fijo, elegido al azar;
// si está ocupado, se prueba con otro.
internal sealed class ContenedorInterrumpible : IAsyncDisposable
{
    private readonly IContainer contenedor;
    private readonly Dictionary<int, int> puertos;

    private ContenedorInterrumpible(IContainer contenedor, Dictionary<int, int> puertos)
    {
        this.contenedor = contenedor;
        this.puertos = puertos;
    }

    public string Host => contenedor.Hostname;

    // El puerto del host en el que se publica un puerto del contenedor. No cambia al detenerlo y volver a iniciarlo.
    public int Puerto(int interno) => puertos[interno];

    public static async Task<ContenedorInterrumpible> IniciarAsync(string imagen, int[] internos,
        Func<ContainerBuilder, ContainerBuilder> configurar)
    {
        for (var intento = 1; ; intento++)
        {
            var asignados = internos.ToDictionary(p => p, _ => Random.Shared.Next(41000, 49000));
            var builder = new ContainerBuilder(imagen);
            foreach (var (interno, externo) in asignados)
            {
                builder = builder.WithPortBinding(externo, interno);
            }

            var contenedor = configurar(builder).Build();
            try
            {
                await contenedor.StartAsync();
                return new ContenedorInterrumpible(contenedor, asignados);
            }
            catch (Exception) when (intento < 5)
            {
                // Lo más probable: el puerto elegido estaba ocupado en el host.
                await contenedor.DisposeAsync();
            }
        }
    }

    // La interrupción deliberada: docker stop.
    public Task DetenerAsync() => contenedor.StopAsync();

    public Task ReanudarAsync() => contenedor.StartAsync();

    public ValueTask DisposeAsync() => contenedor.DisposeAsync();
}

internal static class Interrupciones
{
    // Repite la consulta hasta que devuelva algo, o falla al vencer el plazo con lo que se esperaba.
    public static async Task<T> EsperarAsync<T>(Func<Task<T?>> consulta, TimeSpan plazo, string esperado) where T : class
    {
        var limite = DateTime.UtcNow + plazo;
        do
        {
            if (await consulta() is { } resultado)
            {
                return resultado;
            }

            await Task.Delay(250);
        } while (DateTime.UtcNow < limite);

        throw new TimeoutException($"Pasaron {plazo.TotalSeconds} s y no se cumplió: {esperado}.");
    }

    // El Portal del comercio de demostración, con sesión iniciada en el operador Demo.
    public static async Task<HttpClient> PortalAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> api)
    {
        var portal = api.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(portal, "comercio@tiendademo.uy");
        await Sesion.ElegirOperadorAsync(portal, DatosIniciales.OperadorDemoId);
        return portal;
    }

    // CU-10 desde el Portal: lee el tarifario y las reglas vigentes (caché), guarda el envío y su evento en el outbox.
    public static Task<HttpResponseMessage> CrearEnvioAsync(HttpClient portal, string? email = null) =>
        portal.PostAsJsonAsync("/api/envios", new CrearEnvioRequest(
            new DestinatarioRequest("Ana Resiliencia", "099123456", email, null),
            new DireccionRequest("Av. Italia", Guid.NewGuid().ToString("N")[..8], "Montevideo", "Montevideo", "11300", null),
            [new BultoRequest(2, 30, 20, 10)]));

    // El Worker, como en Logistica.Worker/Program.cs: el módulo Seguimiento y el consumidor de RabbitMQ.
    public static IHost CrearWorker(string postgres, string rabbitMq, string? smtp = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = postgres,
            ["ConnectionStrings:RabbitMQ"] = rabbitMq,
            ["ConnectionStrings:Smtp"] = smtp,
            ["Seguimiento:Correo:Remitente"] = "avisos@logistica.local",
            ["Seguimiento:ClaveToken"] = Convert.ToBase64String(new byte[32]),
            ["Aplicacion:UrlPublica"] = "https://logistica.example",
        });
        builder.Services.AddScoped<InquilinoMensaje>();
        builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<InquilinoMensaje>());
        builder.Services.AddSeguimientoModule(builder.Configuration);
        builder.Services.AddHostedService<ConsumidorRabbitMq>();
        return builder.Build();
    }
}

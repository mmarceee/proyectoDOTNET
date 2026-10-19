using System.Net;
using System.Net.Http.Json;
using Logistica.Http.Contracts.Repartidor;
using Logistica.Repartidor.Pwa.Services;
using Microsoft.JSInterop;

namespace Logistica.IntegrationTests.Ejecucion;

public sealed class MisRutasServiceTests
{
    [Fact]
    public async Task Una_respuesta_tardia_no_muestra_tarjetas_despues_de_cambiar_cuenta()
    {
        var modulo = new Modulo();
        var consultada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continuar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Servidor(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("sesion")) return Json(Modulo.Sesion);
            consultada.SetResult(); await continuar.Task;
            return Json(new MisRutasResponse(Modulo.Sesion.FechaOperativa, [Tarjeta()]));
        })) { BaseAddress = new Uri("https://localhost") };
        await using var hoja = new HojaDeRutaService(http, modulo);
        await using var cola = new CargaService(modulo, hoja);
        await using var rutas = new MisRutasService(http, modulo, cola);
        var cargando = rutas.ActualizarAsync();
        await consultada.Task.WaitAsync(TimeSpan.FromSeconds(5));
        modulo.Activa = Modulo.Sesion with { UsuarioId = Guid.NewGuid(), RepartidorId = Guid.NewGuid() };
        continuar.SetResult(); await cargando.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(rutas.Rutas);
        Assert.Empty(rutas.EstadosLocales);
        Assert.True(rutas.RequiereIngreso);
    }

    [Fact]
    public async Task Sin_servidor_solo_muestra_las_hojas_guardadas_de_la_cuenta_activa()
    {
        var modulo = new Modulo();
        using var http = new HttpClient(new Servidor(_ => throw new HttpRequestException("Sin red"))) { BaseAddress = new Uri("https://localhost") };
        await using var hoja = new HojaDeRutaService(http, modulo);
        await using var cola = new CargaService(modulo, hoja);
        await using var rutas = new MisRutasService(http, modulo, cola);
        await rutas.ActualizarAsync();
        Assert.True(rutas.SinConexion);
        Assert.Equal(Modulo.Ruta, Assert.Single(rutas.Rutas).RutaId);
        Assert.True(Assert.Single(rutas.Rutas).CargaPendiente);
        Assert.Equal("Pendiente", Assert.Single(rutas.EstadosLocales).Resultado);
        rutas.InvalidarEspacio();
        Assert.Empty(rutas.Rutas);
        Assert.Empty(rutas.EstadosLocales);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Un_error_HTTP_no_se_trata_como_falta_de_red(HttpStatusCode estado)
    {
        var modulo = new Modulo();
        using var http = new HttpClient(new Servidor(_ => Task.FromResult(new HttpResponseMessage(estado)))) { BaseAddress = new Uri("https://localhost") };
        await using var hoja = new HojaDeRutaService(http, modulo);
        await using var cola = new CargaService(modulo, hoja);
        await using var rutas = new MisRutasService(http, modulo, cola);
        await rutas.ActualizarAsync();
        Assert.False(rutas.SinConexion);
        Assert.Empty(rutas.Rutas);
        Assert.Equal(estado != HttpStatusCode.ServiceUnavailable, rutas.RequiereIngreso);
        if (rutas.RequiereIngreso) Assert.Null(modulo.Activa);
    }

    private static TarjetaRutaResponse Tarjeta() => new(Modulo.Ruta, Modulo.Sesion.FechaOperativa, "Despachada", "PRUEBA", 1, 1, true, false, false);
    private static HttpResponseMessage Json<T>(T valor) => new(HttpStatusCode.OK) { Content = JsonContent.Create(valor) };
    private sealed class Servidor(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => responder(request);
    }
    private sealed class Modulo : IJSRuntime, IJSObjectReference
    {
        public static readonly Guid Ruta = Guid.NewGuid();
        public static readonly SesionRepartidorResponse Sesion = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 19), "America/Montevideo", DateTimeOffset.UtcNow);
        public SesionRepartidorResponse? Activa = Sesion;
        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => InvokeAsync<T>(id, CancellationToken.None, args);
        public ValueTask<T> InvokeAsync<T>(string id, CancellationToken ct, object?[]? args)
        {
            if (id == "bloquear") Activa = null;
            object? valor = id switch
            {
                "import" => this,
                "sesionLocal" => Activa,
                "estadosLocalesRutas" => new EstadoLocalRuta[] { new(Ruta, "carga", "Pendiente") },
                "rutasGuardadas" => new CopiaHoja[] { new(new(1, Sesion.UsuarioId, Sesion.OperadorId, Sesion.RepartidorId, Ruta, 2,
                    Sesion.FechaOperativa.AddDays(-2), Sesion.FechaOperativa, Sesion.ZonaHorariaOperador, "Despachada", DateTimeOffset.UtcNow,
                    new(Guid.NewGuid(), "PRUEBA"), [], [new(Guid.NewGuid(), 1, "Pendiente", "Calle 10", null, null,
                        [new(Guid.NewGuid(), "ENV-PRUEBA", "AsignadoARuta", "Demo", "099123456", new("Calle", "10", "Montevideo", "Montevideo", "11300", null), null, [], null!)])]), DateTimeOffset.UtcNow, false) },
                _ => null
            };
            return ValueTask.FromResult(valor is null ? default! : (T)valor);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

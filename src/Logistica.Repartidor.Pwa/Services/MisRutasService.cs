using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Logistica.Http.Contracts.Repartidor;
using Microsoft.JSInterop;

namespace Logistica.Repartidor.Pwa.Services;

public sealed record EstadoLocalRuta(Guid RutaId, string Tipo, string? Resultado);

public sealed class MisRutasService(HttpClient http, IJSRuntime js, CargaService cola) : IAsyncDisposable
{
    private IJSObjectReference? modulo;
    private DotNetObjectReference<MisRutasService>? referencia;
    private SesionRepartidorResponse? propietaria;
    private readonly SemaphoreSlim mutex = new(1, 1);
    public IReadOnlyList<TarjetaRutaResponse> Rutas { get; private set; } = [];
    public IReadOnlyList<EstadoLocalRuta> EstadosLocales { get; private set; } = [];
    public DateOnly? FechaOperativa { get; private set; }
    public bool Cargando { get; private set; }
    public bool SinConexion { get; private set; }
    public bool RequiereIngreso { get; private set; }
    public string? Error { get; private set; }
    public event Action? Cambio;

    public async Task ActualizarAsync(int pagina = 1)
    {
        await mutex.WaitAsync(); Cargando = true; Error = null; SinConexion = false; RequiereIngreso = false;
        Rutas = []; EstadosLocales = []; Cambio?.Invoke();
        try
        {
            if (modulo is null)
            {
                modulo = await js.InvokeAsync<IJSObjectReference>("import", "/repartidor/js/hoja-ruta.js");
                referencia = DotNetObjectReference.Create(this);
                await modulo.InvokeVoidAsync("escuchar", referencia);
                cola.Cambio += ColaCambio;
            }
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var sesion = await http.GetAsync("/api/repartidor/sesion", limite.Token);
            if (await RechazadaAsync(sesion)) return;
            sesion.EnsureSuccessStatusCode();
            var identidad = await sesion.Content.ReadFromJsonAsync<SesionRepartidorResponse>(limite.Token) ?? throw new JsonException();
            var anterior = await modulo.InvokeAsync<SesionRepartidorResponse?>("sesionLocal");
            if (anterior is null || anterior.UsuarioId != identidad.UsuarioId || anterior.OperadorId != identidad.OperadorId
                || anterior.RepartidorId != identidad.RepartidorId || anterior.FechaOperativa != identidad.FechaOperativa)
                await modulo.InvokeAsync<string>("activar", identidad);
            FechaOperativa = identidad.FechaOperativa;
            propietaria = identidad;
            using var respuesta = await http.GetAsync($"/api/repartidor/rutas?pagina={pagina}", limite.Token);
            if (await RechazadaAsync(respuesta)) return;
            respuesta.EnsureSuccessStatusCode();
            var datos = await respuesta.Content.ReadFromJsonAsync<MisRutasResponse>(limite.Token) ?? throw new JsonException();
            var estados = await modulo.InvokeAsync<EstadoLocalRuta[]>("estadosLocalesRutas");
            if (!MismaCuenta(await modulo.InvokeAsync<SesionRepartidorResponse?>("sesionLocal"), identidad))
            { InvalidarEspacio(); return; }
            Rutas = datos.Rutas; FechaOperativa = datos.FechaOperativa;
            EstadosLocales = estados;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JSException or JsonException)
        {
            SinConexion = e is HttpRequestException { StatusCode: null } or TaskCanceledException;
            Error = SinConexion ? "Sin conexión. Mostramos las hojas guardadas en este dispositivo; los estados se verificarán al reconectar."
                : "No pudimos actualizar tus rutas. Volvé a intentar.";
            if (SinConexion && modulo is not null)
            {
                try
                {
                    var identidad = await modulo.InvokeAsync<SesionRepartidorResponse?>("sesionLocal");
                    if (identidad is null) { RequiereIngreso = true; return; }
                    FechaOperativa = await modulo.InvokeAsync<DateOnly>("fechaOperativaActual", identidad);
                    propietaria = identidad;
                    var copias = await modulo.InvokeAsync<CopiaHoja[]>("rutasGuardadas");
                    Rutas = copias.OrderBy(c => c.Hoja.FechaRuta).Select(c => new TarjetaRutaResponse(c.Hoja.RutaId,
                        c.Hoja.FechaRuta, c.SinRuta ? "SinRuta" : c.Hoja.EstadoRuta, c.Hoja.Vehiculo.Matricula,
                        c.Hoja.Paradas.Count, c.Hoja.Paradas.Sum(p => p.Envios.Count),
                        c.Hoja.Paradas.SelectMany(p => p.Envios).Any(e => e.Estado == "AsignadoARuta"),
                        c.Hoja.Rendicion?.Iniciada is not null, c.Hoja.Rendicion?.Iniciada?.FinalizadaEn is not null)).ToArray();
                    EstadosLocales = await modulo.InvokeAsync<EstadoLocalRuta[]>("estadosLocalesRutas");
                    if (!MismaCuenta(await modulo.InvokeAsync<SesionRepartidorResponse?>("sesionLocal"), identidad)) InvalidarEspacio();
                }
                catch (JSException) { Rutas = []; EstadosLocales = []; Error = "No pudimos abrir las hojas cifradas. Volvé a ingresar con conexión."; }
            }
        }
        finally { Cargando = false; mutex.Release(); Cambio?.Invoke(); }
    }
    private async Task<bool> RechazadaAsync(HttpResponseMessage r)
    {
        if (r.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)) return false;
        RequiereIngreso = true; Rutas = []; EstadosLocales = [];
        await modulo!.InvokeVoidAsync("bloquear");
        Error = "Iniciá sesión con una cuenta de repartidor habilitada.";
        return true;
    }
    private void ColaCambio() { if (!cola.Ocupado) _ = ReleerAsync(); }
    private static bool MismaCuenta(SesionRepartidorResponse? a, SesionRepartidorResponse? b) => a is not null && b is not null
        && a.UsuarioId == b.UsuarioId && a.OperadorId == b.OperadorId && a.RepartidorId == b.RepartidorId;
    [JSInvokable]
    public void InvalidarEspacio()
    {
        Rutas = []; EstadosLocales = []; RequiereIngreso = true; propietaria = null;
        Error = "La sesión cambió. Volvé a ingresar para ver tus rutas."; Cambio?.Invoke();
    }
    private async Task ReleerAsync()
    {
        await mutex.WaitAsync();
        try
        {
            if (modulo is not null)
            {
                if (!MismaCuenta(await modulo.InvokeAsync<SesionRepartidorResponse?>("sesionLocal"), propietaria)) InvalidarEspacio();
                else EstadosLocales = await modulo.InvokeAsync<EstadoLocalRuta[]>("estadosLocalesRutas");
            }
        }
        catch (JSException) { EstadosLocales = []; }
        finally { mutex.Release(); Cambio?.Invoke(); }
    }
    public async ValueTask DisposeAsync()
    {
        cola.Cambio -= ColaCambio;
        if (modulo is not null)
        {
            if (referencia is not null) await modulo.InvokeVoidAsync("dejarDeEscuchar", referencia);
            await modulo.DisposeAsync();
        }
        referencia?.Dispose();
        mutex.Dispose();
    }
}

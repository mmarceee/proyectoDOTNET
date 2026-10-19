using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Logistica.Http.Contracts.Repartidor;
using Microsoft.JSInterop;

namespace Logistica.Repartidor.Pwa.Services;

public sealed record CopiaHoja(HojaDeRutaResponse Hoja, DateTimeOffset DescargadaEn, bool SinRuta);

public sealed class HojaDeRutaService(HttpClient http, IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? modulo;
    private DotNetObjectReference<HojaDeRutaService>? referencia;
    private readonly SemaphoreSlim mutex = new(1, 1);
    public CopiaHoja? Copia { get; private set; }
    public SesionRepartidorResponse? Sesion { get; private set; }
    public string? Error { get; private set; }
    public bool SinConexion { get; private set; }
    public bool RequiereIngreso { get; private set; }
    public bool SinRuta { get; private set; }
    public bool Cargando { get; private set; }
    public Guid? RutaSeleccionada { get; private set; }
    public event Action? Cambio;

    private async Task InicializarAsync()
    {
        if (modulo is not null) return;
        modulo = await js.InvokeAsync<IJSObjectReference>("import", "/repartidor/js/hoja-ruta.js");
        referencia = DotNetObjectReference.Create(this);
        await modulo.InvokeVoidAsync("escuchar", referencia);
    }

    public Task ActualizarAsync() => ActualizarAsync(null);
    public async Task SeleccionarAsync(Guid? rutaId)
    {
        await ActualizarAsync(rutaId);
    }
    private async Task ActualizarAsync(Guid? rutaId)
    {
        await mutex.WaitAsync();
        Cargando = true; Error = null; SinConexion = false; RequiereIngreso = false; Copia = null; SinRuta = false;
        Cambio?.Invoke();
        try
        {
            await InicializarAsync();
            if (rutaId is Guid seleccion)
            {
                // Una primera visita puede no tener identidad local aún: se selecciona después de verificarla.
                RutaSeleccionada = seleccion;
            }
            else RutaSeleccionada ??= await modulo!.InvokeAsync<Guid?>("rutaSeleccionada");
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var identidad = await http.GetAsync("/api/repartidor/sesion", limite.Token);
            if (await RechazadaAsync(identidad)) return;
            identidad.EnsureSuccessStatusCode();
            Sesion = await identidad.Content.ReadFromJsonAsync<SesionRepartidorResponse>(limite.Token)
                ?? throw new JsonException();
            var anterior = await modulo!.InvokeAsync<SesionRepartidorResponse?>("sesionLocal");
            if (rutaId is null && (anterior is null || anterior.UsuarioId != Sesion.UsuarioId
                || anterior.OperadorId != Sesion.OperadorId || anterior.RepartidorId != Sesion.RepartidorId))
                RutaSeleccionada = null;
            var token = await modulo!.InvokeAsync<string>("activar", Sesion);
            if (RutaSeleccionada is Guid elegida)
            {
                await modulo!.InvokeVoidAsync("seleccionarRuta", elegida);
                token = await modulo!.InvokeAsync<string>("activar", Sesion);
            }
            using var respuesta = await http.GetAsync(RutaSeleccionada is Guid id
                ? $"/api/repartidor/rutas/{id}/hoja-de-ruta" : "/api/repartidor/hoja-de-ruta", limite.Token);
            if (await RechazadaAsync(respuesta)) return;
            if (respuesta.StatusCode == HttpStatusCode.NoContent)
            {
                await modulo!.InvokeVoidAsync("sinRuta", token);
                SinRuta = true;
                Copia = await modulo!.InvokeAsync<CopiaHoja?>("cargar");
                return;
            }
            respuesta.EnsureSuccessStatusCode();
            var hoja = await respuesta.Content.ReadFromJsonAsync<HojaDeRutaResponse>(limite.Token)
                ?? throw new JsonException();
            Copia = await modulo!.InvokeAsync<CopiaHoja>("guardar", hoja, token);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or JSException)
        {
            if (RequiereIngreso)
            {
                Copia = null; Sesion = null;
                Error = "Iniciá sesión con una cuenta de repartidor habilitada para volver a abrir tu hoja.";
                return;
            }
            // Sólo un fallo de transporte sin estado HTTP indica falta de red. Un 503 no es modo offline.
            SinConexion = e is HttpRequestException { StatusCode: null } or TaskCanceledException;
            Error = SinConexion ? "No hay conexión con el servidor. Mostramos la última hoja guardada, si existe."
                : "No pudimos actualizar y guardar la hoja completa. Se conserva la copia anterior.";
            try
            {
                if (modulo is not null)
                {
                    Sesion = await modulo.InvokeAsync<SesionRepartidorResponse?>("sesionLocal");
                    if (Sesion is null)
                    {
                        RequiereIngreso = true;
                        Error = "Necesitás iniciar sesión con conexión antes de abrir una hoja guardada.";
                        return;
                    }
                    if (rutaId is Guid elegida) await modulo.InvokeVoidAsync("seleccionarRuta", elegida);
                    Copia = await modulo.InvokeAsync<CopiaHoja?>("cargar");
                    SinRuta = Copia?.SinRuta == true;
                }
            }
            catch (JSException)
            {
                Copia = null;
                Error = "No pudimos abrir el almacenamiento cifrado. Necesitás conexión y un navegador con almacenamiento local habilitado.";
            }
        }
        finally
        {
            Cargando = false;
            mutex.Release();
            Cambio?.Invoke();
        }
    }

    private async Task<bool> RechazadaAsync(HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)) return false;
        Copia = null; Sesion = null; RequiereIngreso = true;
        RutaSeleccionada = null;
        await modulo!.InvokeVoidAsync("bloquear");
        Error = "Iniciá sesión con una cuenta de repartidor habilitada para volver a abrir tu hoja.";
        return true;
    }

    [JSInvokable]
    public void InvalidarEspacio()
    {
        Copia = null; Sesion = null;
        RutaSeleccionada = null;
        RequiereIngreso = true;
        Error = "La sesión cambió o se cerró en otra pestaña. Volvé a ingresar para abrir tu hoja.";
        Cambio?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        referencia?.Dispose();
        if (modulo is not null) await modulo.DisposeAsync();
        mutex.Dispose();
    }
}

using Microsoft.JSInterop;

namespace Logistica.Repartidor.Pwa.Services;

public sealed class SeguimientoPosicionService(IJSRuntime js, HojaDeRutaService hoja) : IAsyncDisposable
{
    private IJSObjectReference? modulo;
    private DotNetObjectReference<SeguimientoPosicionService>? referencia;
    public string? Estado { get; private set; }
    public event Action? Cambio;

    public async Task InicializarAsync()
    {
        if (modulo is not null) return;
        modulo = await js.InvokeAsync<IJSObjectReference>("import", "/repartidor/js/seguimiento-posicion.js");
        referencia = DotNetObjectReference.Create(this);
        hoja.Cambio += Actualizar;
        await modulo.InvokeVoidAsync("iniciar", referencia);
    }
    private async void Actualizar()
    {
        try { if (modulo is not null && !hoja.Cargando) await modulo.InvokeVoidAsync("actualizar"); }
        catch (JSException) { Estado = "No se pudo actualizar el seguimiento de ubicación."; Cambio?.Invoke(); }
    }
    [JSInvokable]
    public void EstadoSeguimiento(string? estado) { Estado = estado; Cambio?.Invoke(); }
    [JSInvokable]
    public Task ActualizarHojaSeguimiento() => hoja.ActualizarAsync();

    public async ValueTask DisposeAsync()
    {
        hoja.Cambio -= Actualizar;
        if (modulo is not null) { await modulo.InvokeVoidAsync("detener"); await modulo.DisposeAsync(); }
        referencia?.Dispose();
    }
}

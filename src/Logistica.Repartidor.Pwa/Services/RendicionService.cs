using Microsoft.JSInterop;

namespace Logistica.Repartidor.Pwa.Services;

public sealed record BultoDeclarado(Guid EnvioId, string NumeroEnvio, Guid BultoId, string Codigo,
    string Situacion, bool RequiereDeclaracion, bool? Devuelve, string? Observacion);
public sealed record BorradorRendicion(Guid RutaId, long Revision, IReadOnlyList<BultoDeclarado> Bultos,
    string? Observaciones, Guid? OperacionId, string? Resultado, string? Detalle);

public sealed class RendicionService(IJSRuntime js, HojaDeRutaService hoja, CargaService cola) : IAsyncDisposable
{
    private IJSObjectReference? modulo;
    private readonly SemaphoreSlim mutex = new(1, 1);
    public BorradorRendicion? Borrador { get; private set; }
    public string? Mensaje { get; private set; }
    public bool Ocupado { get; private set; }
    public event Action? Cambio;

    public Task AbrirAsync(bool revisar = false, Guid? rutaId = null) => Ejecutar(async () =>
    {
        await hoja.SeleccionarAsync(rutaId);
        if (hoja.RequiereIngreso || hoja.Copia is null) { Borrador = null; return; }
        Borrador = null;
        Borrador = await modulo!.InvokeAsync<BorradorRendicion>("abrirRendicion", hoja.Copia.Hoja.RutaId, revisar);
    });
    public Task DeclararAsync(BultoDeclarado bulto, bool devuelve, string? observacion) => Ejecutar(async () =>
    {
        if (Borrador is null) return;
        Borrador = await modulo!.InvokeAsync<BorradorRendicion>("declararBulto", Borrador.RutaId, Borrador.Revision,
            bulto.BultoId, devuelve, observacion);
    });
    public Task ObservarAsync(string? observaciones) => Ejecutar(async () =>
    {
        if (Borrador is null) return;
        Borrador = await modulo!.InvokeAsync<BorradorRendicion>("observarRendicion", Borrador.RutaId, Borrador.Revision, observaciones);
    });
    public async Task ConfirmarAsync()
    {
        await Ejecutar(async () =>
        {
            if (Borrador is null) return;
            Borrador = await modulo!.InvokeAsync<BorradorRendicion>("confirmarRendicion", Borrador.RutaId, Borrador.Revision);
            Mensaje = "Declaración guardada en este dispositivo, pendiente de sincronizar.";
        });
        if (Borrador?.Resultado == "Pendiente") await SincronizarAsync();
    }
    public async Task SincronizarAsync()
    {
        await cola.SincronizarAsync();
        await RecuperarAsync();
    }
    private void ColaCambio() { if (!cola.Ocupado) _ = RecuperarAsync(); }
    private void SesionCambio()
    {
        if (!hoja.RequiereIngreso) return;
        Borrador = null; Mensaje = null; Cambio?.Invoke();
    }
    private Task RecuperarAsync() => Ejecutar(async () =>
    {
        if (hoja.RequiereIngreso) { Borrador = null; return; }
        if (Borrador is not null)
            Borrador = await modulo!.InvokeAsync<BorradorRendicion?>("borradorRendicion", Borrador.RutaId);
        Mensaje = cola.Mensaje;
    });
    private async Task Ejecutar(Func<Task> accion)
    {
        await mutex.WaitAsync(); Ocupado = true; Cambio?.Invoke();
        try
        {
            if (modulo is null)
            {
                modulo = await js.InvokeAsync<IJSObjectReference>("import", "/repartidor/js/hoja-ruta.js");
                cola.Cambio += ColaCambio; hoja.Cambio += SesionCambio;
            }
            Mensaje = null;
            await accion();
        }
        catch (JSException e) { Mensaje = e.Message.Split('\n')[0]; }
        finally { Ocupado = false; mutex.Release(); Cambio?.Invoke(); }
    }
    public async ValueTask DisposeAsync()
    {
        cola.Cambio -= ColaCambio; hoja.Cambio -= SesionCambio;
        if (modulo is not null) await modulo.DisposeAsync();
        mutex.Dispose();
    }
}

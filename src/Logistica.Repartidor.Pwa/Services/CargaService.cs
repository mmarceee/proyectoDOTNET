using Logistica.Http.Contracts.Repartidor;
using Microsoft.JSInterop;

namespace Logistica.Repartidor.Pwa.Services;

public sealed record BorradorCarga(Guid RutaId, long RevisionRuta, long Revision, IReadOnlyList<LecturaCargaRequest> Lecturas,
    Guid? OperacionId, string? Resultado, string? Detalle);
public sealed record ResumenSincronizacion(string? Mensaje, int Aplicadas);

public sealed class CargaService(IJSRuntime js, HojaDeRutaService hoja) : IAsyncDisposable
{
    private IJSObjectReference? modulo;
    private DotNetObjectReference<CargaService>? referencia;
    private readonly SemaphoreSlim mutex = new(1,1);
    public BorradorCarga? Borrador { get; private set; }
    public string? Mensaje { get; private set; }
    public bool Ocupado { get; private set; }
    public event Action? Cambio;

    public async Task InicializarAsync()
    {
        if (modulo is not null) return;
        modulo = await js.InvokeAsync<IJSObjectReference>("import", "/repartidor/js/hoja-ruta.js");
        referencia = DotNetObjectReference.Create(this);
        await modulo.InvokeVoidAsync("escucharConexion",referencia);
        hoja.Cambio += SesionCambio;
    }
    private void SesionCambio()
    {
        if (!hoja.RequiereIngreso)
        {
            if (!hoja.Cargando && hoja.Sesion is not null) _ = SincronizarAsync();
            return;
        }
        Borrador = null; Mensaje = null; Cambio?.Invoke();
    }
    public Task AbrirAsync() => AbrirRutaAsync(null);
    public async Task AbrirRutaAsync(Guid? rutaId)
    {
        await Ejecutar(async () =>
        {
            if (rutaId is not null || hoja.Copia is null) await hoja.SeleccionarAsync(rutaId);
            if (hoja.RequiereIngreso || hoja.Copia is null) { Borrador = null; return; }
            Borrador = null;
            Borrador = await modulo!.InvokeAsync<BorradorCarga>("abrirCarga", hoja.Copia.Hoja.RutaId);
            if (Borrador.Resultado is null) Mensaje = null;
        });
    }
    public Task LeerAsync(string codigo) => Ejecutar(async () =>
    {
        if (Borrador is null) return;
        Borrador = await modulo!.InvokeAsync<BorradorCarga>("escanearCarga",Borrador.RutaId,Borrador.Revision,codigo);
        var esperado = hoja.Copia?.Hoja.Paradas.SelectMany(p => p.Envios).Where(e => e.Estado == "AsignadoARuta")
            .SelectMany(e => e.Bultos).Any(b => b.Codigo == codigo.Trim()) == true;
        Mensaje = esperado ? "Bulto registrado y guardado." : "Bulto sobrante: separalo de la carga. Quedó registrado.";
    });
    public async Task ConfirmarAsync(bool aceptaFaltantes)
    {
        await Ejecutar(async () =>
        {
            if (Borrador is null) return;
            Borrador = await modulo!.InvokeAsync<BorradorCarga>("confirmarCarga",Borrador.RutaId,Borrador.Revision,aceptaFaltantes);
            Mensaje = "Carga guardada en este dispositivo, pendiente de sincronizar.";
        });
        if (Borrador?.Resultado == "Pendiente") await SincronizarAsync();
    }
    [JSInvokable]
    public Task Reconectar() => SincronizarAsync();
    public Task SincronizarAsync() => Ejecutar(async () =>
    {
        var resultado = await modulo!.InvokeAsync<ResumenSincronizacion>("sincronizarCargas");
        if (resultado.Mensaje is not null) Mensaje = resultado.Mensaje;
        if (Borrador is not null) Borrador = await modulo!.InvokeAsync<BorradorCarga?>("borradorCarga",Borrador.RutaId);
        if (resultado.Aplicadas > 0) await hoja.ActualizarAsync();
    });
    public Task RevisarAsync() => Ejecutar(async () =>
    {
        if (Borrador is null) return;
        await hoja.ActualizarAsync();
        if (hoja.SinConexion || hoja.RequiereIngreso || hoja.Error is not null) return;
        Borrador = await modulo!.InvokeAsync<BorradorCarga>("revisarRechazo",Borrador.RutaId,Borrador.Revision);
        Mensaje = "Revisá y escaneá nuevamente. La confirmación rechazada se conserva como evidencia.";
    });
    private async Task Ejecutar(Func<Task> accion)
    {
        await mutex.WaitAsync();
        Ocupado=true;
        Cambio?.Invoke();
        try { await InicializarAsync(); await accion(); }
        catch (JSException e) { Mensaje=e.Message.Split('\n')[0]; }
        finally { Ocupado=false; mutex.Release(); Cambio?.Invoke(); }
    }
    public async ValueTask DisposeAsync()
    {
        hoja.Cambio -= SesionCambio;
        if (modulo is not null) { await modulo.InvokeVoidAsync("dejarConexion"); await modulo.DisposeAsync(); }
        referencia?.Dispose(); mutex.Dispose();
    }
}

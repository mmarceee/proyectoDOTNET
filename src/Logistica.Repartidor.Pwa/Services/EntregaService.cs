using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Logistica.Repartidor.Pwa.Services;

// Lo que este dispositivo registró de un envío: Pendiente o Enviando (falta sincronizar), Aplicada o Rechazada.
public sealed record EstadoEnvioLocal(Guid EnvioId, string Tipo, string Estado, string? Motivo);

// CU-17 y CU-18 en la PWA. La evidencia la arma evidencia.js y la operación queda cifrada en la cola de hoja-ruta.js;
// después se intenta sincronizar con la misma cola de la carga (CargaService), que también reintenta al volver la
// conexión. Sin conexión, el registro queda pendiente y no se pierde.
public sealed class EntregaService(IJSRuntime js, CargaService cargas) : IAsyncDisposable
{
    private IJSObjectReference? almacen;
    private IJSObjectReference? evidencia;

    private async Task InicializarAsync()
    {
        almacen ??= await js.InvokeAsync<IJSObjectReference>("import", "/repartidor/js/hoja-ruta.js");
        evidencia ??= await js.InvokeAsync<IJSObjectReference>("import", "/repartidor/js/evidencia.js");
        await cargas.InicializarAsync();
    }

    public async Task<IReadOnlyList<EstadoEnvioLocal>> EstadosAsync(Guid rutaId)
    {
        await InicializarAsync();
        return await almacen!.InvokeAsync<EstadoEnvioLocal[]>("estadosEnvios", rutaId);
    }

    public async Task IniciarFirmaAsync(ElementReference canvas)
    {
        await InicializarAsync();
        await evidencia!.InvokeVoidAsync("iniciarFirma", canvas);
    }

    public async Task LimpiarFirmaAsync(ElementReference canvas)
    {
        await InicializarAsync();
        await evidencia!.InvokeVoidAsync("limpiarFirma", canvas);
    }

    // null si quedó registrada; si no, el motivo para mostrarle al repartidor.
    public Task<string?> RegistrarEntregaAsync(Guid rutaId, Guid envioId, ElementReference firma, ElementReference foto,
        string? nombreReceptor, string? documentoReceptor)
        => RegistrarAsync(() => evidencia!.InvokeAsync<EstadoEnvioLocal>("confirmarEntrega", rutaId, envioId, firma, foto,
            nombreReceptor, documentoReceptor).AsTask());

    public Task<string?> RegistrarIntentoAsync(Guid rutaId, Guid envioId, Guid motivoId, ElementReference foto,
        string? observaciones)
        => RegistrarAsync(() => evidencia!.InvokeAsync<EstadoEnvioLocal>("confirmarIntento", rutaId, envioId, motivoId, foto,
            observaciones).AsTask());

    private async Task<string?> RegistrarAsync(Func<Task<EstadoEnvioLocal>> registrar)
    {
        try
        {
            await InicializarAsync();
            await registrar();
        }
        catch (JSException e)
        {
            return e.Message.Split('\n')[0];
        }

        // Ya quedó guardado en el dispositivo. Si no hay conexión, CargaService lo deja pendiente y lo reintenta.
        await cargas.SincronizarAsync();
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (evidencia is not null) await evidencia.DisposeAsync();
        if (almacen is not null) await almacen.DisposeAsync();
    }
}

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Logistica.Http.Contracts.Repartidor;

namespace Logistica.Repartidor.Pwa.Services;

// Lo que este dispositivo registró de un envío: Pendiente o Enviando (falta sincronizar), Aplicada o Rechazada.
public sealed record EstadoEnvioLocal(Guid EnvioId, string Tipo, string Estado, string? Motivo,
    string? Detalle = null, IReadOnlyList<BultoRechazadoResponse>? Bultos = null,
    IReadOnlyList<ResultadoBultoRequest>? Resultados = null);
public sealed record BultoVisitaLocal(string CodigoBulto, bool Entregado, Guid? MotivoId);
public sealed record BultoExcluidoLocal(string CodigoBulto, string Estado);
public sealed record BorradorVisitaLocal(Guid RutaId, Guid EnvioId, long Revision,
    IReadOnlyList<BultoVisitaLocal> Bultos, IReadOnlyList<BultoExcluidoLocal> Excluidos);

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

    public async Task<InicioRutaLocal> InicioAsync(Guid rutaId)
    {
        await InicializarAsync();
        return await almacen!.InvokeAsync<InicioRutaLocal>("estadoInicioRuta", rutaId);
    }

    public async Task<BorradorVisitaLocal> AbrirVisitaAsync(Guid rutaId, Guid envioId)
    {
        await InicializarAsync();
        return await almacen!.InvokeAsync<BorradorVisitaLocal>("abrirVisita", rutaId, envioId);
    }

    public async Task<BorradorVisitaLocal> ResultadoAsync(BorradorVisitaLocal borrador, string codigo, bool entregado, Guid? motivo)
    {
        await InicializarAsync();
        return await almacen!.InvokeAsync<BorradorVisitaLocal>("resultadoVisita", borrador.RutaId, borrador.EnvioId,
            borrador.Revision, codigo, entregado, motivo);
    }

    public Task<string?> RegistrarVisitaAsync(BorradorVisitaLocal borrador, ElementReference firma, ElementReference foto,
        string? nombre, string? documento, string? observaciones) => RegistrarAsync(() =>
        evidencia!.InvokeAsync<EstadoEnvioLocal>("confirmarVisita", borrador.RutaId, borrador.EnvioId, borrador.Revision,
            borrador.Bultos.Any(b => b.Entregado), firma, foto, nombre, documento, observaciones).AsTask());

    public async Task IniciarFirmaAsync(ElementReference canvas, object? notificarCambios = null)
    {
        await InicializarAsync();
        await evidencia!.InvokeVoidAsync("iniciarFirma", canvas, notificarCambios);
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

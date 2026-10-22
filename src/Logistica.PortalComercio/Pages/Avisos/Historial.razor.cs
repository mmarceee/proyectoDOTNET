using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Logistica.Http.Contracts.Avisos;
using Microsoft.AspNetCore.Components;

namespace Logistica.PortalComercio.Pages.Avisos;

public partial class Historial
{
    private const string Ruta = "/api/avisos/entregas";
    private const string PaginaHistorial = "/portal/integraciones/avisos/historial";
    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private NavigationManager Navegacion { get; set; } = default!;
    [Parameter] public Guid? EntregaId { get; set; }
    [SupplyParameterFromQuery] public string? Estado { get; set; }
    [SupplyParameterFromQuery] public string? Desde { get; set; }
    [SupplyParameterFromQuery] public string? Hasta { get; set; }
    [SupplyParameterFromQuery] public int? Pagina { get; set; }
    private readonly CancellationTokenSource _vida = new();
    private CancellationTokenSource? _consulta;
    private CancellationToken _cancelacion;
    private int _revision;
    private HistorialAvisosResponse? _historial;
    private DetalleEntregaAvisoResponse? _detalle;
    private readonly HashSet<Guid> _seleccionados = [];
    private List<Guid> _porConfirmar = [];
    private string _estado = "";
    private DateTime? _desde;
    private DateTime? _hasta;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;
    private bool _cargando;
    private bool _elegirOperador;
    private bool AccionesBloqueadas => _ocupado || _porConfirmar.Count > 0;
    private string Query => Consulta(Estado, Desde, Hasta, Pagina ?? 1);
    private string UrlLista => PaginaHistorial + Query;
    private string UrlDetalle(Guid id) => $"{PaginaHistorial}/{id}{Query}";
    private bool TodosSeleccionados => _historial?.Entregas.Any(e => e.PuedeReenviar) == true &&
        _historial.Entregas.Where(e => e.PuedeReenviar).All(e => _seleccionados.Contains(e.Id));

    protected override void OnInitialized() => _cancelacion = _vida.Token;
    protected override Task OnParametersSetAsync()
    {
        _estado = Estado ?? "";
        _desde = Parsear(Desde);
        _hasta = Parsear(Hasta);
        _mensaje = null;
        return CargarAsync();
    }

    private async Task CargarAsync()
    {
        _consulta?.Cancel();
        _consulta?.Dispose();
        var revision = ++_revision;
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(_cancelacion);
        _consulta = limite;
        limite.CancelAfter(TimeSpan.FromSeconds(25));
        _ocupado = true;
        _cargando = true;
        _error = null;
        _historial = null;
        _detalle = null;
        _seleccionados.Clear();
        _porConfirmar = [];
        try
        {
            using var respuesta = await Http.GetAsync(EntregaId is { } id ? $"{Ruta}/{id}" : Ruta + Query, limite.Token);
            if (revision != _revision) return;
            if (!respuesta.IsSuccessStatusCode) { await MostrarErrorAsync(respuesta, limite.Token); return; }
            if (EntregaId is not null)
            {
                var detalle = await respuesta.Content.ReadFromJsonAsync<DetalleEntregaAvisoResponse>(limite.Token) ?? throw new JsonException();
                if (revision == _revision) _detalle = detalle;
            }
            else
            {
                var historial = await respuesta.Content.ReadFromJsonAsync<HistorialAvisosResponse>(limite.Token) ?? throw new JsonException();
                if (revision == _revision) _historial = historial;
            }
            _elegirOperador = false;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException)
        {
            if (!_cancelacion.IsCancellationRequested && revision == _revision) _error = "No se pudo cargar el historial. Intentá actualizarlo nuevamente.";
        }
        finally
        {
            if (revision == _revision) { _ocupado = false; _cargando = false; _consulta = null; }
        }
    }

    private Task FiltrarAsync()
    {
        Navegacion.NavigateTo(PaginaHistorial + Consulta(_estado, Dia(_desde), Dia(_hasta), 1));
        return Task.CompletedTask;
    }

    private Task LimpiarAsync() { _estado = ""; _desde = null; _hasta = null; return FiltrarAsync(); }
    private void Paginar(int pagina) => Navegacion.NavigateTo(PaginaHistorial + Consulta(Estado, Desde, Hasta, pagina));
    private void Seleccionar(Guid id, bool valor) { if (valor) _seleccionados.Add(id); else _seleccionados.Remove(id); }
    private void SeleccionarTodos(ChangeEventArgs e)
    {
        _seleccionados.Clear();
        if (e.Value is true && _historial is not null) _seleccionados.UnionWith(_historial.Entregas.Where(x => x.PuedeReenviar).Select(x => x.Id));
    }
    private void Confirmar(IEnumerable<Guid> ids) { _porConfirmar = ids.Distinct().ToList(); _error = null; _mensaje = null; }
    private void Cancelar() => _porConfirmar = [];

    private async Task ReenviarAsync()
    {
        if (_ocupado || _porConfirmar.Count == 0) return;
        _ocupado = true;
        _error = null;
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(_cancelacion);
            limite.CancelAfter(TimeSpan.FromSeconds(30));
            using var tokenRespuesta = await Http.GetAsync("/api/avisos/suscripciones/antifalsificacion", limite.Token);
            if (!tokenRespuesta.IsSuccessStatusCode) { await MostrarErrorAsync(tokenRespuesta, limite.Token); return; }
            var token = await tokenRespuesta.Content.ReadFromJsonAsync<TokenAntifalsificacionResponse>(limite.Token) ?? throw new JsonException();
            using var peticion = new HttpRequestMessage(HttpMethod.Post, _porConfirmar.Count == 1
                ? $"{Ruta}/{_porConfirmar[0]}/reenvio" : $"{Ruta}/reenvios");
            peticion.Headers.Add("RequestVerificationToken", token.Token);
            if (_porConfirmar.Count > 1) peticion.Content = JsonContent.Create(new ReenviarAvisosRequest(_porConfirmar));
            using var respuesta = await Http.SendAsync(peticion, limite.Token);
            if (!respuesta.IsSuccessStatusCode) { await MostrarErrorAsync(respuesta, limite.Token); return; }
            _porConfirmar = [];
            var resultado = await respuesta.Content.ReadFromJsonAsync<ReenvioAvisosResponse>(limite.Token) ?? throw new JsonException();
            _mensaje = $"{resultado.Cantidad} reenvío(s) solicitado(s). El sistema los procesará en segundo plano; actualizá para consultar el resultado.";
            await CargarAsync();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException)
        {
            _porConfirmar = [];
            if (!_cancelacion.IsCancellationRequested) _error = "No se pudo confirmar el resultado. Actualizá el historial antes de intentar de nuevo; no se reintentó automáticamente.";
        }
        finally { _ocupado = false; }
    }

    private async Task MostrarErrorAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        if (respuesta.StatusCode == HttpStatusCode.Unauthorized) { Navegacion.NavigateTo("/portal/ingresar", forceLoad: true); return; }
        _porConfirmar = [];
        _elegirOperador = respuesta.StatusCode == HttpStatusCode.Forbidden;
        _error = respuesta.StatusCode switch
        {
            HttpStatusCode.Forbidden => "No tenés acceso con la sesión actual. Verificá el operador seleccionado.",
            HttpStatusCode.NotFound => "Algún aviso no está disponible. Actualizá el historial.",
            HttpStatusCode.Conflict => "Los avisos cambiaron. Actualizá el historial antes de confirmar nuevamente.",
            _ => "No se pudo completar la operación. Actualizá el historial e intentá nuevamente."
        };
        if (respuesta.StatusCode == HttpStatusCode.BadRequest)
        {
            try { var p = await respuesta.Content.ReadFromJsonAsync<Problema>(ct); _error = p?.Detail ?? p?.Title ?? _error; }
            catch (JsonException) { }
        }
    }

    private static string Consulta(string? estado, string? desde, string? hasta, int pagina)
    {
        List<string> partes = ["pagina=" + pagina.ToString(CultureInfo.InvariantCulture)];
        if (!string.IsNullOrEmpty(estado)) partes.Add("estado=" + Uri.EscapeDataString(estado));
        if (!string.IsNullOrEmpty(desde)) partes.Add("desde=" + Uri.EscapeDataString(desde));
        if (!string.IsNullOrEmpty(hasta)) partes.Add("hasta=" + Uri.EscapeDataString(hasta));
        return "?" + string.Join("&", partes);
    }
    private static DateTime? Parsear(string? valor) => DateTime.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha) ? fecha : null;
    private static string? Dia(DateTime? fecha) => fecha?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Fecha(DateTimeOffset fecha) => fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    private static string NombreEstado(string estado) => estado == "EnColaDeFallidos" ? "En cola de fallidos" : estado;
    private static string NombreTipo(string tipo) => tipo switch { "CambioDeEstado" => "Cambio de estado", "EntregaFallida" => "Entrega fallida", "Devolucion" => "Devolución", _ => tipo };
    private sealed record Problema(string? Title, string? Detail);
    public void Dispose() { ++_revision; _vida.Cancel(); _consulta?.Cancel(); _vida.Dispose(); }
}

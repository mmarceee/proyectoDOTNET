using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Logistica.Http.Contracts.Avisos;
using Microsoft.AspNetCore.Components;

namespace Logistica.PortalComercio.Pages.Avisos;

public partial class Index
{
    private const string Ruta = "/api/avisos/suscripciones";
    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private NavigationManager Navegacion { get; set; } = default!;
    private readonly CancellationTokenSource _vida = new();
    private CancellationToken _cancelacion;
    private NuevaSuscripcionFormulario _formulario = new();
    private IReadOnlyList<SuscripcionAvisoResponse>? _suscripciones;
    private SecretoSuscripcionAvisoResponse? _secreto;
    private SuscripcionAvisoResponse? _confirmacion;
    private SuscripcionAvisoResponse? _seguridad;
    private ResultadoPruebaAvisoResponse? _resultadoPrueba;
    private string? _urlPrueba;
    private Guid? _probando;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;
    private bool _cargando;
    private bool _mostrarFormulario;
    private bool _regenerar;
    private bool _elegirOperador;
    private bool AccionesBloqueadas => _ocupado || _secreto is not null || _confirmacion is not null || _mostrarFormulario;

    protected override Task OnInitializedAsync()
    {
        _cancelacion = _vida.Token;
        return CargarAsync();
    }

    private async Task CargarAsync()
    {
        if (_ocupado) return;
        _ocupado = true;
        _cargando = true;
        _error = null;
        _confirmacion = null;
        _seguridad = null;
        _resultadoPrueba = null;
        try { await LeerListaAsync(); }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException)
        {
            if (!_cancelacion.IsCancellationRequested) _error = "No se pudo cargar la lista. Intentá actualizarla de nuevo.";
        }
        finally { _cargando = false; _ocupado = false; }
    }

    private async Task LeerListaAsync()
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(_cancelacion);
        limite.CancelAfter(TimeSpan.FromSeconds(20));
        using var respuesta = await Http.GetAsync(Ruta, limite.Token);
        if (!respuesta.IsSuccessStatusCode) { await MostrarErrorAsync(respuesta); return; }
        _suscripciones = await respuesta.Content.ReadFromJsonAsync<List<SuscripcionAvisoResponse>>(limite.Token)
            ?? throw new JsonException();
        _elegirOperador = false;
    }

    private void AbrirFormulario()
    {
        _formulario = new();
        _mostrarFormulario = true;
        _confirmacion = null;
        _seguridad = null;
        _mensaje = null;
        _error = null;
    }

    private void CancelarFormulario() => _mostrarFormulario = false;
    private void CerrarSecreto() => _secreto = null;
    private void CancelarAccion() => _confirmacion = null;
    private void CerrarConfiguracion() => _seguridad = null;

    private void AbrirConfiguracion(SuscripcionAvisoResponse suscripcion)
    {
        _seguridad = suscripcion;
        _confirmacion = null;
        _error = null;
        _mensaje = null;
    }

    private void PedirConfirmacion(SuscripcionAvisoResponse suscripcion, bool regenerar)
    {
        _confirmacion = suscripcion;
        _regenerar = regenerar;
        _error = null;
        _mensaje = null;
    }

    private Task CrearAsync() => EscribirAsync(null, _formulario.ARequest());

    private async Task EnviarPruebaAsync(SuscripcionAvisoResponse suscripcion)
    {
        if (AccionesBloqueadas || !suscripcion.Activa) return;
        _ocupado = true;
        _probando = suscripcion.Id;
        _resultadoPrueba = null;
        _urlPrueba = suscripcion.UrlDestino;
        _error = null;
        _mensaje = null;
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(_cancelacion);
            limite.CancelAfter(TimeSpan.FromSeconds(20));
            using var tokenRespuesta = await Http.GetAsync($"{Ruta}/antifalsificacion", limite.Token);
            if (!tokenRespuesta.IsSuccessStatusCode) { await MostrarErrorAsync(tokenRespuesta); return; }
            var token = await tokenRespuesta.Content.ReadFromJsonAsync<TokenAntifalsificacionResponse>(limite.Token)
                ?? throw new JsonException();
            using var peticion = new HttpRequestMessage(HttpMethod.Post, $"{Ruta}/{suscripcion.Id}/prueba");
            peticion.Headers.Add("RequestVerificationToken", token.Token);
            using var respuesta = await Http.SendAsync(peticion, limite.Token);
            if (!respuesta.IsSuccessStatusCode) { await MostrarErrorAsync(respuesta); return; }
            _resultadoPrueba = await respuesta.Content.ReadFromJsonAsync<ResultadoPruebaAvisoResponse>(limite.Token)
                ?? throw new JsonException();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException)
        {
            if (!_cancelacion.IsCancellationRequested)
                _error = "No se pudo obtener el resultado de la prueba. El receptor podría haberla recibido. No se reintentó automáticamente.";
        }
        finally { _ocupado = false; _probando = null; }
    }

    private Task ConfirmarAccionAsync() => _confirmacion is null ? Task.CompletedTask
        : EscribirAsync($"/{_confirmacion.Id}/{(_regenerar ? "secreto" : "desactivacion")}", null);

    private async Task EscribirAsync(string? sufijo, CrearSuscripcionAvisoRequest? alta)
    {
        if (_ocupado) return;
        _ocupado = true;
        _error = null;
        _mensaje = null;
        _resultadoPrueba = null;
        var guardado = false;
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(_cancelacion);
            limite.CancelAfter(TimeSpan.FromSeconds(30));
            using var tokenRespuesta = await Http.GetAsync($"{Ruta}/antifalsificacion", limite.Token);
            if (!tokenRespuesta.IsSuccessStatusCode) { await MostrarErrorAsync(tokenRespuesta); return; }
            var token = await tokenRespuesta.Content.ReadFromJsonAsync<TokenAntifalsificacionResponse>(limite.Token)
                ?? throw new JsonException();
            using var peticion = new HttpRequestMessage(HttpMethod.Post, Ruta + sufijo);
            peticion.Headers.Add("RequestVerificationToken", token.Token);
            if (alta is not null) peticion.Content = JsonContent.Create(alta);
            using var respuesta = await Http.SendAsync(peticion, limite.Token);
            if (!respuesta.IsSuccessStatusCode) { await MostrarErrorAsync(respuesta); return; }
            guardado = true;
            _mostrarFormulario = false;
            _confirmacion = null;
            _seguridad = null;
            if (respuesta.StatusCode != HttpStatusCode.NoContent)
                _secreto = await respuesta.Content.ReadFromJsonAsync<SecretoSuscripcionAvisoResponse>(limite.Token)
                    ?? throw new JsonException();
            _mensaje = alta is not null ? "Se creó la suscripción." : _regenerar
                ? "Se generó una nueva clave de firma. Actualizala en tu sistema." : "Se desactivó la suscripción.";
            await LeerListaAsync();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException)
        {
            if (!_cancelacion.IsCancellationRequested)
                _error = guardado
                    ? _secreto is not null
                        ? "El cambio se guardó, pero no se pudo actualizar la lista. Guardá la clave y luego actualizá la pantalla."
                        : "El cambio se guardó. Actualizá la lista; si no recibiste la clave, tendrás que generar una nueva."
                    : "No se pudo confirmar el resultado. Actualizá la lista antes de volver a intentar; si se generó una clave y no la recibiste, tendrás que generar una nueva.";
        }
        finally { _ocupado = false; }
    }

    private async Task MostrarErrorAsync(HttpResponseMessage respuesta)
    {
        if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
        {
            _secreto = null;
            _resultadoPrueba = null;
            _seguridad = null;
            _confirmacion = null;
            _suscripciones = null;
            Navegacion.NavigateTo("/portal/ingresar", forceLoad: true);
            return;
        }
        if (respuesta.StatusCode == HttpStatusCode.Forbidden)
        {
            _secreto = null;
            _resultadoPrueba = null;
            _seguridad = null;
            _confirmacion = null;
            _suscripciones = null;
            _elegirOperador = true;
            _error = "No tenés acceso con la sesión actual. Verificá el operador seleccionado.";
            return;
        }
        _error = respuesta.StatusCode switch
        {
            HttpStatusCode.NotFound => "La suscripción ya no está disponible. Actualizá la lista.",
            HttpStatusCode.Conflict => "La suscripción cambió mientras trabajabas. Actualizá la lista antes de confirmar de nuevo.",
            _ => "No se pudo completar la operación. Intentá de nuevo."
        };
        if (respuesta.StatusCode == HttpStatusCode.BadRequest)
        {
            try
            {
                var problema = await respuesta.Content.ReadFromJsonAsync<Problema>(_cancelacion);
                _error = problema?.Detail ?? problema?.Title ?? _error;
            }
            catch (JsonException) { }
        }
    }

    private static string NombreEvento(string tipo) => tipo switch
    {
        "CambioDeEstado" => "Cambio de estado",
        "EntregaFallida" => "Entrega fallida",
        "Devolucion" => "Devolución",
        _ => tipo
    };

    private sealed record Problema(string? Title, string? Detail);

    public void Dispose()
    {
        _secreto = null;
        _vida.Cancel();
        _vida.Dispose();
    }
}

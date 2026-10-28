using System.Text.Json;
using Logistica.Modules.Administracion.Application.Features.GestionarMensajesFallidos;
using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarMensajesFallidos;

// CU-09, pasos 3 a 6, y A1: el contenido y el historial de errores de un mensaje; reintentarlo o descartarlo.
// Un mensaje de otro operador no se encuentra: 404 (ADR-0002, sección 2.9).
[Authorize(Policy = Politicas.Administrador)]
internal sealed class DetalleModel(MensajesFallidosHandler handler) : PageModel
{
    private static readonly JsonSerializerOptions Indentado = new() { WriteIndented = true };

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public string? Motivo { get; set; }

    public MensajeFallidoDetalle Detalle { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct) => await MostrarAsync(ct);

    public async Task<IActionResult> OnPostReintentarAsync(CancellationToken ct)
    {
        try
        {
            TempData["Mensaje"] = IndexModel.TextoReintento(await handler.ReintentarAsync([Id], IndexModel.Usuario(User), ct));
            return LocalRedirect("/backoffice/mensajes-fallidos");
        }
        catch (DomainException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            ModelState.AddModelError(string.Empty, "No se pudo publicar en la cola de mensajes. Probá de nuevo en unos minutos.");
        }
        return await MostrarAsync(ct);
    }

    public async Task<IActionResult> OnPostDescartarAsync(CancellationToken ct)
    {
        try
        {
            if (!await handler.DescartarAsync(Id, Motivo, IndexModel.Usuario(User), ct)) return NotFound();
            TempData["Mensaje"] = "Se descartó el mensaje. Queda registrado quién lo descartó y por qué.";
            return LocalRedirect("/backoffice/mensajes-fallidos");
        }
        catch (DomainException e)
        {
            ModelState.AddModelError(nameof(Motivo), e.Message);
            return await MostrarAsync(ct);
        }
    }

    // El contenido es el JSON del evento; se muestra indentado para leerlo.
    public string ContenidoLegible()
    {
        try
        {
            using var json = JsonDocument.Parse(Detalle.Contenido);
            return JsonSerializer.Serialize(json.RootElement, Indentado);
        }
        catch (JsonException)
        {
            return Detalle.Contenido;
        }
    }

    private async Task<IActionResult> MostrarAsync(CancellationToken ct)
    {
        if (await handler.ObtenerAsync(Id, ct) is not { } detalle) return NotFound();
        Detalle = detalle;
        return Page();
    }
}

using System.Security.Claims;
using Logistica.Modules.Administracion.Application.Features.GestionarMensajesFallidos;
using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarMensajesFallidos;

// CU-09, pasos 1 y 2, y A2: los mensajes fallidos del operador, con reintento de varios a la vez.
[Authorize(Policy = Politicas.Administrador)]
internal sealed class IndexModel(MensajesFallidosHandler handler) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public bool VerResueltos { get; set; }

    [BindProperty]
    public List<Guid> Seleccionados { get; set; } = [];

    public IReadOnlyList<MensajeFallidoResumen> Mensajes { get; private set; } = [];
    public string? Mensaje { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Mensaje = TempData["Mensaje"] as string;
        Mensajes = await handler.ListarAsync(VerResueltos, ct);
    }

    public async Task<IActionResult> OnPostReintentarAsync(CancellationToken ct)
    {
        try
        {
            var cantidad = await handler.ReintentarAsync(Seleccionados, Usuario(User), ct);
            TempData["Mensaje"] = TextoReintento(cantidad);
            return LocalRedirect("/backoffice/mensajes-fallidos");
        }
        catch (DomainException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // RabbitMQ no disponible o la cola de origen ya no existe: no se marca como reintentado lo que no se publicó.
            ModelState.AddModelError(string.Empty, "No se pudo publicar en la cola de mensajes. Probá de nuevo en unos minutos.");
        }
        Mensajes = await handler.ListarAsync(VerResueltos, ct);
        return Page();
    }

    internal static string Usuario(ClaimsPrincipal usuario) =>
        usuario.Identity?.Name ?? usuario.FindFirstValue(ClaimTypes.NameIdentifier) ?? "desconocido";

    internal static string TextoReintento(int cantidad) => cantidad switch
    {
        0 => "Los mensajes elegidos ya se habían resuelto.",
        1 => "Se reintentó 1 mensaje. Si vuelve a fallar, aparece de nuevo en la lista.",
        _ => $"Se reintentaron {cantidad} mensajes. Si vuelven a fallar, aparecen de nuevo en la lista.",
    };
}

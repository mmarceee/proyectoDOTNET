using System.Security.Claims;
using Logistica.Modules.Administracion.Application.Features.IniciarSesion;
using Logistica.Modules.Administracion.Application.Features.SeleccionarOperador;
using Logistica.Modules.Administracion.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.SeleccionarOperador;

// CU-03: el usuario de comercio elige con qué operador trabaja en la sesión. La misma página sirve para cambiarlo (A3).
[Authorize(Roles = nameof(Perfil.UsuarioComercio))]
internal sealed class OperadorModel(IOperadoresDelComercio operadores, IServicioSesion sesion) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IReadOnlyList<OperadorDisponible> Operadores { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        // El comercio sale de la cookie, nunca del pedido (ADR-0002, sección 2.4).
        if (!Guid.TryParse(User.FindFirstValue(ClaimsDeSesion.ComercioId), out var comercioId))
        {
            return Forbid();
        }

        Operadores = await operadores.ActivosAsync(comercioId, ct);

        // A1: con una sola relación activa, se elige sola.
        if (Operadores.Count == 1)
        {
            return await ElegirAsync(Operadores[0].Id, ct);
        }

        // A2 (ninguna) o varias: se muestra la página.
        return Page();
    }

    public Task<IActionResult> OnPostAsync(Guid operadorId, CancellationToken ct)
    {
        return ElegirAsync(operadorId, ct);
    }

    private async Task<IActionResult> ElegirAsync(Guid operadorId, CancellationToken ct)
    {
        // El usuario es el de la cookie. Un operador que no es de su comercio responde como si no existiera
        // (ADR-0002, sección 2.9).
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuarioId)
            || !await sesion.SeleccionarOperadorAsync(usuarioId, operadorId, ct))
        {
            return NotFound();
        }

        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/portal/");
    }
}

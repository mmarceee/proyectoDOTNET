using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Seguimiento.Presentation.Features.VerTablero;

// CU-66, paso 1: la página del tablero. No trae datos: el navegador se conecta a /hubs/tablero y pide la foto a
// /api/tablero, con la misma sesión y los mismos perfiles (pre-condición 1).
[Authorize(Roles = "Administrador,Despachador")]
internal sealed class TableroModel : PageModel
{
    public void OnGet()
    {
    }
}

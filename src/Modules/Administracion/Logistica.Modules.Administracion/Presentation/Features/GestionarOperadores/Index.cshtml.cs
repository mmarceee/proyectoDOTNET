using Logistica.Modules.Administracion.Application.Features.GestionarOperadores;
using Logistica.Modules.Administracion.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarOperadores;

// Administrador general: los operadores de la plataforma.
[Authorize(Policy = Politicas.AdministradorGeneral)]
internal sealed class IndexModel(GestionarOperadoresHandler operadores) : PageModel
{
    public IReadOnlyList<OperadorResumen> Operadores { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Operadores = await operadores.ListarAsync(ct);
    }
}

using Logistica.Modules.Administracion.Application.Features.DefinirZonas;
using Logistica.Modules.Administracion.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.DefinirZonas;

// CU-04, paso 2: el listado de zonas del operador. Sólo el perfil Administrador.
[Authorize(Policy = Politicas.Administrador)]
internal sealed class IndexModel(ConsultarZonasHandler consultar) : PageModel
{
    public IReadOnlyList<ZonaResumenDto> Zonas { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Zonas = await consultar.ListarAsync(ct);
    }
}

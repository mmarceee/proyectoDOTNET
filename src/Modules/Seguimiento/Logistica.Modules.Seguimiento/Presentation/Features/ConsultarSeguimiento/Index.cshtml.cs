using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Seguimiento.Presentation.Features.ConsultarSeguimiento;

// CU-60: los envíos del operador para elegir cuál enlace de seguimiento mandar, sin escribir la dirección a mano. Los
// mismos perfiles que la página del enlace: el operario de depósito no entra ni escribiendo la dirección (403).
[Authorize(Roles = "Administrador,Despachador")]
internal sealed class IndexModel(IEnviosModuleApi envios) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public string? Texto { get; set; }

    public PaginaEnviosParaSeguimiento Resultado { get; private set; } = new([], 1, 0);

    public async Task OnGetAsync(CancellationToken ct)
    {
        Resultado = await envios.ListarParaSeguimientoAsync(Texto, Math.Max(Pagina, 1), ct);
    }

    // La dirección de otra página del listado, conservando la búsqueda.
    public string UrlPagina(int pagina) =>
        $"?Pagina={pagina}&Texto={Uri.EscapeDataString(Texto ?? "")}";
}

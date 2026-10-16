using Logistica.Modules.Administracion.Application.Features.GestionarComercios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Comercios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarComercios;

[Authorize(Policy = Politicas.Administrador)]
internal sealed class IndexModel(GestionarComerciosHandler comercios) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Buscar { get; set; }
    [BindProperty(SupportsGet = true)] public EstadoRelacion? Estado { get; set; }
    public IReadOnlyList<ComercioVista> Comercios { get; private set; } = [];
    public async Task OnGetAsync(CancellationToken ct) => Comercios = await comercios.ListarAsync(Buscar, Estado, ct);
}

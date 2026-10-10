using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Planificacion.Application.Features.ConsultarRutas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Planificacion.Presentation.Features.ConsultarRutas;

[TypeFilter(typeof(AccesoPlanificacion))]
internal sealed class IndexModel(ConsultarRutasHandler service, AccesoPlanificacion acceso) : PageModel
{
    [BindProperty(SupportsGet = true)] public DateOnly? Fecha { get; set; }
    public IReadOnlyList<RutaResponse> Rutas { get; private set; } = [];
    public bool Desarrollo => acceso.Desarrollo;
    public async Task OnGetAsync(CancellationToken ct) => Rutas = await service.ListarAsync(Fecha, ct);
}

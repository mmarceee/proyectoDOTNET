using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Planificacion.Application.Rutas;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Planificacion.Presentation.Features.Rutas;

[TypeFilter(typeof(AccesoPlanificacion))]
internal abstract class RutaEditorModel(PlanificacionService service, IServiceScopeFactory scopes, AccesoPlanificacion acceso) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty] public DateOnly Fecha { get; set; }
    [BindProperty] public Guid RepartidorId { get; set; }
    [BindProperty] public Guid VehiculoId { get; set; }
    [BindProperty] public long RevisionEsperada { get; set; }
    [BindProperty] public List<Guid> Seleccion { get; set; } = [];
    [BindProperty] public Guid? ZonaId { get; set; }
    [BindProperty] public Guid? FranjaId { get; set; }
    [BindProperty] public string? Texto { get; set; }
    [BindProperty] public int Pagina { get; set; } = 1;
    public int TotalCandidatos { get; private set; }
    public IReadOnlyDictionary<Guid, string> NumerosEnvio { get; private set; } = new Dictionary<Guid, string>();
    public RecursosPlanificacion Recursos { get; private set; } = new([], [], [], []);
    public IReadOnlyList<EnvioDisponibleResponse> Candidatos { get; private set; } = [];
    public RutaResponse? Ruta { get; private set; }
    public EvaluacionRutaResponse? Evaluacion { get; private set; }
    public IReadOnlyList<ValidacionRutaResponse> Historial { get; private set; } = [];
    public bool Desarrollo => acceso.Desarrollo;
    public bool Editable => Ruta is null || Ruta.Estado == "Planificada";

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        try
        {
            Fecha = await service.HoyAsync(ct);
            if (Id is Guid id)
            {
                Ruta = await service.DetalleAsync(id, ct); Fecha = Ruta.Fecha; RepartidorId = Ruta.RepartidorId;
                VehiculoId = Ruta.VehiculoId; RevisionEsperada = Ruta.Revision;
            }
            await CargarAsync(service, ct);
            return Page();
        }
        catch (RutaNoEncontradaException) { return NotFound(); }
    }
    public async Task<IActionResult> OnPostFiltrarAsync(CancellationToken ct)
    { await CargarAsync(service, ct); return Page(); }
    public async Task<IActionResult> OnPostLimpiarFiltrosAsync(CancellationToken ct)
    {
        ZonaId = null; FranjaId = null; Texto = null; Pagina = 1;
        foreach (var campo in new[] { nameof(ZonaId), nameof(FranjaId), nameof(Texto), nameof(Pagina) })
            ModelState.Remove(campo);
        await CargarAsync(service, ct);
        return Page();
    }
    public async Task<IActionResult> OnPostValidarAsync(CancellationToken ct)
    {
        try { Evaluacion = await service.PrevalidarAsync(new(Fecha, RepartidorId, VehiculoId, Seleccion, Id, Id is null ? null : RevisionEsperada), ct); }
        catch (Exception e) when (Esperada(e)) { MostrarError(e); }
        await CargarFrescoAsync(ct); return Page();
    }
    public Task<IActionResult> OnPostConfirmarAsync(CancellationToken ct) => GuardarAsync(false, ct);
    public Task<IActionResult> OnPostModificarAsync(CancellationToken ct) => GuardarAsync(true, ct);
    private async Task<IActionResult> GuardarAsync(bool modificar, CancellationToken ct)
    {
        if (!ModelState.IsValid) { await CargarFrescoAsync(ct); return Page(); }
        try
        {
            RutaResponse ruta;
            if (Id is not Guid id) ruta = await service.CrearAsync(new(Fecha, RepartidorId, VehiculoId, Seleccion), ct);
            else if (modificar) ruta = await service.ModificarAsync(id, new(RevisionEsperada, Fecha, RepartidorId, VehiculoId), ct);
            else
            {
                var actual = await service.DetalleAsync(id, ct);
                if (actual.Fecha != Fecha || actual.RepartidorId != RepartidorId || actual.VehiculoId != VehiculoId)
                    throw new DomainException("Confirmá primero el cambio de fecha o recursos y después agregá los envíos.");
                ruta = await service.AgregarAsync(id, new(RevisionEsperada, Seleccion), ct);
            }
            return Redirect($"/backoffice/rutas/{ruta.Id}");
        }
        catch (RutaNoEncontradaException) { return NotFound(); }
        catch (Exception e) when (Esperada(e)) { MostrarError(e); }
        await CargarFrescoAsync(ct); return Page();
    }
    private static bool Esperada(Exception e) => e is DomainException or ConflictoRutaException or RutaNoEncontradaException;
    private void MostrarError(Exception e)
    {
        ModelState.AddModelError(string.Empty, e.Message);
        if (e is ConflictoRutaException conflicto)
        {
            Seleccion.RemoveAll(id => conflicto.EnvioIds.Contains(id));
            ModelState.Remove(nameof(Seleccion));
        }
    }
    private async Task CargarFrescoAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        await CargarAsync(scope.ServiceProvider.GetRequiredService<PlanificacionService>(), ct);
    }
    private async Task CargarAsync(PlanificacionService lector, CancellationToken ct)
    {
        Recursos = await lector.RecursosAsync(Fecha, ct);
        Pagina = Math.Max(1, Pagina);
        var visibles = await lector.DisponiblesAsync(Fecha, ZonaId, FranjaId, Texto, Pagina, 100, ct);
        TotalCandidatos = visibles.Total;
        var seleccionados = new List<EnvioDisponibleResponse>();
        for (var pagina = 1; pagina <= (Seleccion.Count + 99) / 100; pagina++)
            seleccionados.AddRange((await lector.DisponiblesAsync(Fecha, null, null, null, pagina, 100, ct, Seleccion)).Items);
        Candidatos = visibles.Items.Concat(seleccionados).DistinctBy(e => e.Id).ToList();
        if (Id is Guid id)
        {
            Ruta = await lector.DetalleAsync(id, ct);
            Historial = await lector.ValidacionesAsync(id, 1, ct);
            NumerosEnvio = await lector.NumerosEnvioAsync(Ruta.Paradas.Select(p => p.EnvioId)
                .Concat(Historial.SelectMany(v => v.Bultos).Select(b => b.EnvioId)).Distinct().ToArray(), ct);
            // Una nueva confirmación debe usar la revisión recién mostrada, sin reenviar automáticamente.
            RevisionEsperada = Ruta.Revision; ModelState.Remove(nameof(RevisionEsperada));
        }
    }
}

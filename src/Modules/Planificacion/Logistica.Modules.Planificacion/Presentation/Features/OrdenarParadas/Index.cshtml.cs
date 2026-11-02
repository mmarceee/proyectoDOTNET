using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Planificacion.Application.Exceptions;
using Logistica.Modules.Planificacion.Application.Features.OrdenarParadas;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Planificacion.Presentation.Features.OrdenarParadas;

[TypeFilter(typeof(AccesoPlanificacion))]
internal sealed class IndexModel(OrdenarParadasHandler handler, IServiceScopeFactory scopes) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public long RevisionEsperada { get; set; }
    [BindProperty] public List<Guid> ParadaIds { get; set; } = [];
    [BindProperty] public string? Movimiento { get; set; }
    [BindProperty] public bool ConfirmarAdvertencias { get; set; }
    public OrdenParadasResponse? Propuesta { get; private set; }

    public Task<IActionResult> OnGetAsync(CancellationToken ct) => EjecutarAsync(async () =>
        Mostrar(await handler.ConsultarAsync(Id, ct)), ct);

    public Task<IActionResult> OnPostProponerAsync(CancellationToken ct) => EjecutarAsync(async () =>
        Mostrar(await handler.ProponerAsync(Id, RevisionEsperada, ct)), ct);

    public Task<IActionResult> OnPostMoverAsync(CancellationToken ct) => EjecutarAsync(async () =>
    {
        // El servidor vuelve a validar pertenencia, duplicados, revisión y estado de la ruta.
        var movimiento = Movimiento?.Split(':');
        if (movimiento is not { Length: 2 } || !Guid.TryParse(movimiento[0], out var id)
            || !int.TryParse(movimiento[1], out var posicion) || posicion < 1 || posicion > ParadaIds.Count
            || !ParadaIds.Contains(id)) throw new DomainException("El movimiento de la parada no es válido.");
        var orden = ParadaIds.ToList();
        orden.Remove(id); orden.Insert(posicion - 1, id);
        Mostrar(await handler.RevisarAsync(Id, RevisionEsperada, orden, ct));
    }, ct);

    public async Task<IActionResult> OnPostGuardarAsync(CancellationToken ct)
    {
        try
        {
            if (!ModelState.IsValid) throw new DomainException("Revisá los datos del orden propuesto.");
            await handler.GuardarAsync(Id, RevisionEsperada, ParadaIds, ConfirmarAdvertencias, ct);
            TempData["MensajeConfirmacion"] = "Orden de las paradas guardado.";
            return Redirect($"/backoffice/rutas/{Id}#paradas-actuales");
        }
        catch (RutaNoEncontradaException) { return NotFound(); }
        catch (Exception e) when (e is DomainException or ConflictoRutaException)
        {
            return await RecuperarAsync(e, ct);
        }
    }

    private async Task<IActionResult> EjecutarAsync(Func<Task> accion, CancellationToken ct)
    {
        try
        {
            if (!ModelState.IsValid) throw new DomainException("Revisá los datos del orden propuesto.");
            await accion();
            return Page();
        }
        catch (RutaNoEncontradaException) { return NotFound(); }
        catch (Exception e) when (e is DomainException or ConflictoRutaException) { return await RecuperarAsync(e, ct); }
    }

    private async Task<IActionResult> RecuperarAsync(Exception error, CancellationToken ct)
    {
        // Evitar volver a mostrar entidades rastreadas por la transacción que acaba de fallar.
        await using var scope = scopes.CreateAsyncScope();
        var lector = scope.ServiceProvider.GetRequiredService<OrdenarParadasHandler>();
        try
        {
            // Un error de confirmación conserva el borrador válido; un conflicto recarga la ruta actual.
            if (error is DomainException && RevisionEsperada > 0 && ParadaIds.Count > 0)
                Mostrar(await lector.RevisarAsync(Id, RevisionEsperada, ParadaIds, ct));
            else Mostrar(await lector.ConsultarAsync(Id, ct));
        }
        catch (RutaNoEncontradaException) { return NotFound(); }
        catch (Exception e) when (e is DomainException or ConflictoRutaException)
        {
            try { Mostrar(await lector.ConsultarAsync(Id, ct)); }
            catch (RutaNoEncontradaException) { return NotFound(); }
            catch (Exception actual) when (actual is DomainException or ConflictoRutaException)
            { Propuesta = null; ModelState.Clear(); }
        }
        ModelState.AddModelError("", error.Message);
        return Page();
    }

    private void Mostrar(OrdenParadasResponse propuesta)
    {
        Propuesta = propuesta; RevisionEsperada = propuesta.Revision;
        ParadaIds = propuesta.Paradas.Select(p => p.Id).ToList();
        ConfirmarAdvertencias = false;
        ModelState.Clear();
    }
}

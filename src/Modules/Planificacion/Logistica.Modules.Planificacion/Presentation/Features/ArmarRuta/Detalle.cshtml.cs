using Logistica.Modules.Planificacion.Application.Features.ArmarRuta;
using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Planificacion.Application.Features.ConsultarRutas;
using Logistica.Modules.Planificacion.Application.Features.ConsultarEnviosDisponibles;
using Logistica.Modules.Planificacion.Application.Features.ConsultarRecursosPlanificacion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Logistica.Modules.Planificacion.Application.Features.QuitarEnvioDeRuta;
using Logistica.Modules.Planificacion.Application.Exceptions;
using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Presentation.Features.ArmarRuta;
internal sealed class DetalleModel(ArmarRutaHandler service, ConsultarRutasHandler consultas, ConsultarEnviosDisponiblesHandler disponibles, ConsultarRecursosPlanificacionHandler recursos, IServiceScopeFactory scopes, AccesoPlanificacion acceso,
    QuitarEnvioDeRutaHandler quitar, Logistica.Modules.Planificacion.Application.Features.DespacharRuta.DespacharRutaHandler despachar,
    Logistica.Modules.Planificacion.Application.Features.AnularDespacho.AnularDespachoHandler anular)
    : RutaEditorModel(service, consultas, disponibles, recursos, scopes, acceso)
{
    [BindProperty] public Guid EnvioAQuitar { get; set; }
    [BindProperty] public string? MotivoAnulacion { get; set; }
    [BindProperty] public bool ConfirmoSinCargaPendiente { get; set; }
    public bool PuedeAnularDespacho => Ruta?.Estado == "Despachada" && Ruta.Fecha < Hoy;
    public bool ConfirmacionDespacho { get; private set; }
    public EvaluacionRutaResponse? EvaluacionDespacho { get; private set; }

    public async Task<IActionResult> OnPostAnularDespachoAsync(CancellationToken ct)
    {
        if (Id is not Guid id) return NotFound();
        try
        {
            if (!ModelState.IsValid) { await CargarFrescoAsync(ct); return Page(); }
            await anular.HandleAsync(new(id, RevisionEsperada, MotivoAnulacion ?? "", ConfirmoSinCargaPendiente), ct);
            MensajeConfirmacion = "Despacho anulado. La ruta volvió a Planificada y sus envíos siguen asignados. Modificá la fecha o quitá los envíos antes de volver a despachar.";
            return Redirect($"/backoffice/rutas/{id}#resultado-ruta");
        }
        catch (RutaNoEncontradaException) { return NotFound(); }
        catch (Exception e) when (e is DomainException or ConflictoRutaException)
        {
            await CargarFrescoAsync(ct);
            MostrarError(e);
            return Page();
        }
    }

    public Task<IActionResult> OnPostPrepararDespachoAsync(CancellationToken ct) => ProcesarDespachoAsync(false, ct);
    public Task<IActionResult> OnPostDespacharAsync(CancellationToken ct) => ProcesarDespachoAsync(true, ct);
    private async Task<IActionResult> ProcesarDespachoAsync(bool confirmar, CancellationToken ct)
    {
        if (Id is not Guid id) return NotFound();
        try
        {
            if (!ModelState.IsValid) { await CargarFrescoAsync(ct); return Page(); }
            if (confirmar)
            {
                await despachar.HandleAsync(new(id, RevisionEsperada), ct);
                MensajeConfirmacion = "Ruta despachada. Se registró el aviso interno; el repartidor podrá verlo cuando termine de procesarse.";
                return Redirect($"/backoffice/rutas/{id}#resultado-ruta");
            }
            var revision = RevisionEsperada;
            EvaluacionDespacho = await despachar.PrevalidarAsync(new(id, revision), ct);
            await CargarFrescoAsync(ct);
            if (Ruta?.Revision != revision)
                throw new DomainException("Otra operación modificó la ruta. Revisá y validá nuevamente el despacho.");
            ConfirmacionDespacho = true;
            return Page();
        }
        catch (RutaNoEncontradaException) { return NotFound(); }
        catch (Exception e) when (e is DomainException or ConflictoRutaException)
        {
            await CargarFrescoAsync(ct);
            MostrarError(e);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostQuitarEnvioAsync(CancellationToken ct)
    {
        if (Id is not Guid id) return NotFound();
        try
        {
            if (!ModelState.IsValid)
            {
                await CargarFrescoAsync(ct);
                return Page();
            }
            var ruta = await quitar.HandleAsync(new(id, EnvioAQuitar, RevisionEsperada), ct);
            MensajeConfirmacion = ruta.Paradas.Count == 0
                ? "Envío quitado. Volvió a En depósito y la ruta quedó vacía, con sus recursos liberados."
                : "Envío quitado. Volvió a En depósito y se actualizó el orden de las paradas.";
            return Redirect($"/backoffice/rutas/{id}#resultado-ruta");
        }
        catch (RutaNoEncontradaException) { return NotFound(); }
        catch (Exception e) when (e is DomainException or ConflictoRutaException)
        {
            await CargarFrescoAsync(ct);
            MostrarError(e);
            return Page();
        }
    }
}

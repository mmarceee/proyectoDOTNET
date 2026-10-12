using Logistica.Modules.Planificacion.Application.Features.ArmarRuta;
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
    QuitarEnvioDeRutaHandler quitar)
    : RutaEditorModel(service, consultas, disponibles, recursos, scopes, acceso)
{
    [BindProperty] public Guid EnvioAQuitar { get; set; }

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

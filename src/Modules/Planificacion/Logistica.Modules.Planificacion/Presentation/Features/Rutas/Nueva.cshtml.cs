using Logistica.Modules.Planificacion.Application.Rutas;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Planificacion.Presentation.Features.Rutas;
internal sealed class NuevaModel(PlanificacionService service, IServiceScopeFactory scopes, AccesoPlanificacion acceso)
    : RutaEditorModel(service, scopes, acceso);

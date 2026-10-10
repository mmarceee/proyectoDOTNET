using Logistica.Modules.Planificacion.Application.Features.ArmarRuta;
using Logistica.Modules.Planificacion.Application.Features.ConsultarRutas;
using Logistica.Modules.Planificacion.Application.Features.ConsultarEnviosDisponibles;
using Logistica.Modules.Planificacion.Application.Features.ConsultarRecursosPlanificacion;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Planificacion.Presentation.Features.ArmarRuta;
internal sealed class DetalleModel(ArmarRutaHandler service, ConsultarRutasHandler consultas, ConsultarEnviosDisponiblesHandler disponibles, ConsultarRecursosPlanificacionHandler recursos, IServiceScopeFactory scopes, AccesoPlanificacion acceso)
    : RutaEditorModel(service, consultas, disponibles, recursos, scopes, acceso);

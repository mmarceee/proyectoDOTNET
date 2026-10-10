using Logistica.Modules.Administracion.Domain.Planificacion;

namespace Logistica.Modules.Administracion.Application.Features.ConsultarRecursosPlanificacion;

internal sealed record DatosRecursosPlanificacion(IReadOnlyList<Repartidor> Repartidores, IReadOnlyList<Vehiculo> Vehiculos, IReadOnlyList<Zona> Zonas, IReadOnlyList<FranjaHoraria> Franjas);

using Logistica.Modules.Envios.Domain.Incidencias;

namespace Logistica.Modules.Envios.Application.Features.GestionarIncidencias;

// CU-22, A1: el listado de incidencias del operador, filtrado por estado, tipo y fecha de creación (días del operador,
// ambos inclusive).
internal sealed record ConsultarIncidenciasQuery(
    int Pagina = 1,
    int TamanoPagina = 20,
    EstadoIncidencia? Estado = null,
    TipoIncidencia? Tipo = null,
    DateOnly? FechaDesde = null,
    DateOnly? FechaHasta = null);

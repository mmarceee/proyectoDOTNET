namespace Logistica.Modules.Envios.Application.Features.GestionarIncidencias;

// CU-22, A1. Controla los filtros antes de leer; el operador lo pone el filtro "Tenant" (ADR-0002).
internal sealed class ConsultarIncidenciasHandler(IIncidenciasListadoReader reader)
{
    public Task<ConsultarIncidenciasResult> HandleAsync(ConsultarIncidenciasQuery query, CancellationToken ct)
    {
        if (query.Pagina < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(query.Pagina), "La página debe ser mayor que cero.");
        }

        if (query.TamanoPagina < 1 || query.TamanoPagina > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(query.TamanoPagina), "El tamaño de página debe estar entre 1 y 100.");
        }

        if (query.Estado is { } estado && !Enum.IsDefined(estado))
        {
            throw new ArgumentOutOfRangeException(nameof(query.Estado), "El estado indicado no es válido.");
        }

        if (query.Tipo is { } tipo && !Enum.IsDefined(tipo))
        {
            throw new ArgumentOutOfRangeException(nameof(query.Tipo), "El tipo indicado no es válido.");
        }

        if (query.FechaDesde is { } desde && query.FechaHasta is { } hasta && desde > hasta)
        {
            throw new ArgumentException("La fecha desde no puede ser posterior a la fecha hasta.");
        }

        return reader.ConsultarAsync(query, ct);
    }
}

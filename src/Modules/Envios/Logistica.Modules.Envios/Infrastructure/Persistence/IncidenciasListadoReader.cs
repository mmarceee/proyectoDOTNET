using Logistica.Modules.Envios.Application.Features.GestionarIncidencias;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

// CU-22, A1. El aislamiento por operador lo aplica el filtro global "Tenant" en las incidencias y en los envíos
// (ADR-0002); acá sólo están los filtros de la pantalla.
internal sealed class IncidenciasListadoReader(EnviosDbContext db) : IIncidenciasListadoReader
{
    public async Task<ConsultarIncidenciasResult> ConsultarAsync(ConsultarIncidenciasQuery query, CancellationToken ct)
    {
        var consulta = db.Incidencias.AsNoTracking();

        if (query.Estado is { } estado)
        {
            consulta = consulta.Where(i => i.Estado == estado);
        }

        if (query.Tipo is { } tipo)
        {
            consulta = consulta.Where(i => i.Tipo == tipo);
        }

        // Los días son los del operador, igual que en el listado de envíos.
        if (query.FechaDesde is { } desde)
        {
            var inicio = FechasConsultaEnvios.InicioDelDiaUtc(desde);
            consulta = consulta.Where(i => i.CreadaEn >= inicio);
        }

        if (query.FechaHasta is { } hasta && hasta < DateOnly.MaxValue)
        {
            var finExclusivo = FechasConsultaEnvios.InicioDelDiaUtc(hasta.AddDays(1));
            consulta = consulta.Where(i => i.CreadaEn < finExclusivo);
        }

        var total = await consulta.CountAsync(ct);
        var totalPaginas = Math.Max(1, (int)Math.Ceiling((double)total / query.TamanoPagina));
        var pagina = Math.Min(query.Pagina, totalPaginas);

        // Primero se ordena y se corta la página, y después se suma el número del envío: EF no traduce un orden sobre
        // la fila ya proyectada.
        var incidencias = await consulta
            .OrderByDescending(i => i.CreadaEn)
            .ThenByDescending(i => i.Id)
            .Skip((pagina - 1) * query.TamanoPagina)
            .Take(query.TamanoPagina)
            .Join(db.Envios, i => i.EnvioId, e => e.Id, (i, e) => new IncidenciaListadoDto(
                i.Id, e.Numero, i.Tipo.ToString(), i.Descripcion, i.Estado.ToString(), i.CreadaEn, i.ResueltaEn,
                i.Resolucion))
            .ToListAsync(ct);

        return new ConsultarIncidenciasResult(incidencias, pagina, query.TamanoPagina, total);
    }
}

using Logistica.Modules.Envios.Application.Features.ConsultarEnvios;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

internal sealed class EnviosListadoReader(
    EnviosDbContext db,
    ICurrentTenant tenant) : IEnviosListadoReader
{
    public async Task<ConsultarEnviosResult> ConsultarAsync(
        ConsultarEnviosQuery query,
        CancellationToken ct)
    {
        if (tenant.OperadorId is not Guid operadorId)
        {
            return new ConsultarEnviosResult(
                [], 1, query.TamanoPagina, 0);
        }

        var consulta = db.Envios
            .AsNoTracking()
            .Where(e => e.OperadorId == operadorId);

        if (tenant.ComercioId is Guid comercioId)
        {
            consulta = consulta.Where(e => e.ComercioId == comercioId);
        }

        var total = await consulta.CountAsync(ct);

        var totalPaginas = Math.Max(
            1,
            (int)Math.Ceiling((double)total / query.TamanoPagina));

        var pagina = Math.Min(query.Pagina, totalPaginas);

        var envios = await consulta
            .OrderByDescending(e => e.CreadoEn)
            .ThenByDescending(e => e.Id)
            .Skip((pagina - 1) * query.TamanoPagina)
            .Take(query.TamanoPagina)
            .Select(e => new EnvioListadoDto(
                e.Numero,
                e.ComercioId,
                e.Destinatario.Nombre,
                e.Direccion.Localidad,
                e.Estado.ToString(),
                e.CreadoEn,
                e.MontoTarifa))
            .ToListAsync(ct);

        return new ConsultarEnviosResult(
            envios,
            pagina,
            query.TamanoPagina,
            total);
    }
}

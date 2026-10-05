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

        if (query.Estado is { } estado)
        {
            consulta = consulta.Where(e => e.Estado == estado);
        }

        if (query.FechaDesde is { } desde)
        {
            var inicio = InicioDelDiaUtc(desde);

            consulta = consulta.Where(e => e.CreadoEn >= inicio);
        }

        if (query.FechaHasta is { } hasta && hasta < DateOnly.MaxValue)
        {
            var finExclusivo = InicioDelDiaUtc(hasta.AddDays(1));

            consulta = consulta.Where(e => e.CreadoEn < finExclusivo);
        }
        var total = await consulta.CountAsync(ct);

        var totalPaginas = Math.Max(1, (int)Math.Ceiling((double)total / query.TamanoPagina));

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

    private static DateTimeOffset InicioDelDiaUtc(DateOnly fecha)
    {
        var zona = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");

        var medianoche = fecha.ToDateTime(
            TimeOnly.MinValue,
            DateTimeKind.Unspecified);

        var utc = TimeZoneInfo.ConvertTimeToUtc(medianoche, zona);

        return new DateTimeOffset(utc);
    }
}

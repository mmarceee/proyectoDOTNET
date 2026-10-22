using Logistica.Http.Contracts.Avisos;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Application.Features.HistorialAvisos;

internal sealed record HistorialAvisosQuery(EstadoEntregaAviso? Estado, DateTimeOffset? Desde,
    DateTimeOffset? HastaExclusiva, int Pagina, int TamanoPagina);

internal interface IHistorialAvisosReader
{
    Task<HistorialAvisosResponse> ConsultarAsync(HistorialAvisosQuery query, CancellationToken ct);
    Task<DetalleEntregaAvisoResponse?> ObtenerAsync(Guid id, CancellationToken ct);
}

internal sealed class ConsultarHistorialAvisosHandler(IHistorialAvisosReader lector,
    IAdministracionModuleApi administracion, ICurrentTenant tenant)
{
    public async Task<HistorialAvisosResponse> ListarAsync(string? estado, DateOnly? desde, DateOnly? hasta,
        int pagina, CancellationToken ct)
    {
        ExigirComercio();
        if (pagina is < 1 or > 100000) throw new DomainException("La página no es válida.");
        if (desde > hasta || hasta == DateOnly.MaxValue)
            throw new DomainException("El rango de fechas no es válido.");
        EstadoEntregaAviso? filtro = null;
        if (!string.IsNullOrEmpty(estado))
        {
            if (!Enum.TryParse<EstadoEntregaAviso>(estado, out var valor) || !Enum.IsDefined(valor) || estado != valor.ToString())
                throw new DomainException("El estado del aviso no es válido.");
            filtro = valor;
        }
        var zona = TimeZoneInfo.FindSystemTimeZoneById(await administracion.ObtenerZonaHorariaActualAsync(ct));
        DateTimeOffset? inicio = desde is { } d ? LimiteUtc(d, zona) : null;
        DateTimeOffset? fin = hasta is { } h ? LimiteUtc(h.AddDays(1), zona) : null;
        var resultado = await lector.ConsultarAsync(new(filtro, inicio, fin, pagina, 20), ct);
        return resultado with { Entregas = resultado.Entregas.Select(e => Local(e, zona)).ToArray() };
    }

    public async Task<DetalleEntregaAvisoResponse?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        ExigirComercio();
        var resultado = await lector.ObtenerAsync(id, ct);
        if (resultado is null) return null;
        var zona = TimeZoneInfo.FindSystemTimeZoneById(await administracion.ObtenerZonaHorariaActualAsync(ct));
        return resultado with
        {
            Entrega = Local(resultado.Entrega, zona),
            Intentos = resultado.Intentos.Select(i => i with { Fecha = TimeZoneInfo.ConvertTime(i.Fecha, zona) }).ToArray()
        };
    }

    private static DateTimeOffset LimiteUtc(DateOnly dia, TimeZoneInfo zona)
    {
        var fecha = dia.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (zona.IsInvalidTime(fecha)) throw new DomainException("La fecha no es válida en la zona horaria del operador.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(fecha, zona));
    }

    private static EntregaAvisoResumen Local(EntregaAvisoResumen e, TimeZoneInfo zona) =>
        e with { FechaEvento = TimeZoneInfo.ConvertTime(e.FechaEvento, zona) };

    private void ExigirComercio()
    {
        if (tenant.OperadorId is null || tenant.ComercioId is null)
            throw new InvalidOperationException("El historial requiere comercio y operador en la sesión.");
    }
}

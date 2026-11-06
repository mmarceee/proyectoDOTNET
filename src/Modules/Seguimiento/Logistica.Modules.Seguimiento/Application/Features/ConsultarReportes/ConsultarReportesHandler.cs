using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Deposito.Contracts;
using Logistica.Modules.Seguimiento.Domain.Indicadores;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Application.Features.ConsultarReportes;

internal enum TipoReporte
{
    Cumplimiento,
    Motivos,
    Volumen,
    Liquidacion,
}

internal enum DimensionCumplimiento
{
    Zona,
    Repartidor,
    Comercio,
}

// Paso 2: el período son días del operador, ambos incluidos.
internal sealed record ConsultarReporteQuery(TipoReporte Tipo, DateOnly Desde, DateOnly Hasta,
    DimensionCumplimiento Dimension = DimensionCumplimiento.Zona);

// El porcentaje a tiempo cuenta sólo las entregas con ventana comprometida (CU-71).
internal sealed record FilaCumplimiento(string Nombre, int Entregas, int ConVentana, int ATiempo, int IntentosFallidos)
{
    public decimal? PorcentajeATiempo => ConVentana == 0 ? null : Math.Round(100m * ATiempo / ConVentana, 1);
}

internal sealed record FilaMotivo(string Motivo, int Cantidad, decimal Porcentaje);

internal sealed record FilaVolumen(DateOnly Dia, int Altas, int Entregas, int IntentosFallidos);

internal sealed record FilaLiquidacion(string Comercio, int Liquidaciones, int Envios, decimal TotalNeto, decimal Cobrado,
    decimal Pendiente);

// Sólo una de las listas trae filas: la del tipo pedido. Vacío es A1 (período sin datos).
internal sealed record Reporte(ConsultarReporteQuery Query, IReadOnlyList<FilaCumplimiento> Cumplimiento,
    IReadOnlyList<FilaMotivo> Motivos, IReadOnlyList<FilaVolumen> Volumen, IReadOnlyList<FilaLiquidacion> Liquidacion)
{
    public bool Vacio => Cumplimiento.Count + Motivos.Count + Volumen.Count + Liquidacion.Count == 0;
}

// Agregados de la tabla de lectura de los indicadores (CU-71), entre dos instantes: [desde, hasta).
internal sealed record CumplimientoAgrupado(Guid? Clave, int Entregas, int ConVentana, int ATiempo, int IntentosFallidos);

internal sealed record MotivoAgrupado(string Motivo, int Cantidad);

internal sealed record RegistroLeido(TipoRegistroCumplimiento Tipo, DateTimeOffset OcurridoEn);

internal interface IReportesReader
{
    Task<IReadOnlyList<CumplimientoAgrupado>> CumplimientoAsync(DimensionCumplimiento dimension, DateTimeOffset desde,
        DateTimeOffset hasta, CancellationToken ct);

    Task<IReadOnlyList<MotivoAgrupado>> MotivosAsync(DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct);

    Task<IReadOnlyList<RegistroLeido>> RegistrosAsync(DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct);
}

// CU-72: los reportes de gestión. Cumplimiento, motivos y volumen salen de la tabla de lectura de Seguimiento; la
// liquidación, de Depósito y liquidaciones (requerimiento especial 1). Los nombres de zonas, repartidores y comercios
// los da Administración. No modifica datos.
internal sealed class ConsultarReportesHandler(IReportesReader lector, IAdministracionModuleApi administracion,
    IDepositoModuleApi deposito, TimeProvider reloj)
{
    private const string SinDato = "Sin dato";

    public async Task<Reporte> HandleAsync(ConsultarReporteQuery query, CancellationToken ct)
    {
        // A2.
        if (query.Hasta < query.Desde)
            throw new DomainException("La fecha desde es posterior a la fecha hasta.");

        var zona = TimeZoneInfo.FindSystemTimeZoneById(await administracion.ObtenerZonaHorariaActualAsync(ct));
        var desde = Inicio(query.Desde, zona);
        var hasta = Inicio(query.Hasta.AddDays(1), zona);
        var vacio = new Reporte(query, [], [], [], []);

        return query.Tipo switch
        {
            TipoReporte.Cumplimiento => vacio with { Cumplimiento = await CumplimientoAsync(query.Dimension, desde, hasta, ct) },
            TipoReporte.Motivos => vacio with { Motivos = await MotivosAsync(desde, hasta, ct) },
            TipoReporte.Volumen => vacio with { Volumen = await VolumenAsync(desde, hasta, zona, ct) },
            TipoReporte.Liquidacion => vacio with { Liquidacion = await LiquidacionAsync(query.Desde, query.Hasta, ct) },
            _ => throw new DomainException("Elegí un tipo de reporte válido."),
        };
    }

    private async Task<IReadOnlyList<FilaCumplimiento>> CumplimientoAsync(DimensionCumplimiento dimension,
        DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
    {
        var grupos = await lector.CumplimientoAsync(dimension, desde, hasta, ct);
        var nombres = await NombresAsync(dimension, grupos.Where(g => g.Clave is not null).Select(g => g.Clave!.Value).ToList(), ct);
        return grupos
            .Select(g => new FilaCumplimiento(g.Clave is Guid id ? nombres.GetValueOrDefault(id, "Dado de baja") : SinDato,
                g.Entregas, g.ConVentana, g.ATiempo, g.IntentosFallidos))
            .OrderBy(f => f.Nombre == SinDato).ThenBy(f => f.Nombre, StringComparer.CurrentCulture)
            .ToList();
    }

    private async Task<IReadOnlyList<FilaMotivo>> MotivosAsync(DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
    {
        var motivos = await lector.MotivosAsync(desde, hasta, ct);
        var total = motivos.Sum(m => m.Cantidad);
        return motivos
            .Select(m => new FilaMotivo(m.Motivo, m.Cantidad, Math.Round(100m * m.Cantidad / total, 1)))
            .OrderByDescending(m => m.Cantidad).ThenBy(m => m.Motivo, StringComparer.Ordinal)
            .ToList();
    }

    // Por día del operador. Se agrupa en memoria para convertir cada instante a su zona horaria.
    private async Task<IReadOnlyList<FilaVolumen>> VolumenAsync(DateTimeOffset desde, DateTimeOffset hasta,
        TimeZoneInfo zona, CancellationToken ct)
    {
        return (await lector.RegistrosAsync(desde, hasta, ct))
            .GroupBy(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.OcurridoEn, zona).DateTime))
            .Select(g => new FilaVolumen(g.Key, g.Count(r => r.Tipo == TipoRegistroCumplimiento.Alta),
                g.Count(r => r.Tipo == TipoRegistroCumplimiento.Entrega),
                g.Count(r => r.Tipo == TipoRegistroCumplimiento.IntentoFallido)))
            .OrderBy(f => f.Dia)
            .ToList();
    }

    private async Task<IReadOnlyList<FilaLiquidacion>> LiquidacionAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var resumen = await deposito.ResumirLiquidacionesAsync(desde, hasta, ct);
        var comercios = (await administracion.ConsultarComerciosAsync(ct)).ToDictionary(c => c.ComercioId, c => c.RazonSocial);
        return resumen
            .Select(r => new FilaLiquidacion(comercios.GetValueOrDefault(r.ComercioId, "Dado de baja"), r.Liquidaciones, r.Envios,
                r.TotalNeto, r.Cobrado, r.Pendiente))
            .OrderBy(f => f.Comercio, StringComparer.CurrentCulture)
            .ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> NombresAsync(DimensionCumplimiento dimension,
        IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        switch (dimension)
        {
            case DimensionCumplimiento.Repartidor:
                // Incluye a los repartidores desactivados.
                return (await administracion.ConsultarNombresRecursosAsync(ids, [], ct)).Repartidores;
            case DimensionCumplimiento.Comercio:
                return (await administracion.ConsultarComerciosAsync(ct)).ToDictionary(c => c.ComercioId, c => c.RazonSocial);
            default:
                var hoy = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
                return (await administracion.ConsultarRecursosPlanificacionAsync(hoy, ct)).Zonas.ToDictionary(z => z.Id, z => z.Nombre);
        }
    }

    private static DateTimeOffset Inicio(DateOnly dia, TimeZoneInfo zona) =>
        new(TimeZoneInfo.ConvertTimeToUtc(dia.ToDateTime(TimeOnly.MinValue), zona), TimeSpan.Zero);
}

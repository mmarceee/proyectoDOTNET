namespace Logistica.Modules.Envios.Infrastructure.Persistence;

internal static class FechasConsultaEnvios
{
    private static readonly TimeZoneInfo Zona = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");

    public static DateTimeOffset InicioDelDiaUtc(DateOnly fecha) =>
        TimeZoneInfo.ConvertTimeToUtc(fecha.ToDateTime(TimeOnly.MinValue), Zona);
}

using System.Collections.Concurrent;

namespace Logistica.BuildingBlocks.Infrastructure.Observabilidad;

// Duración de las solicitudes de los últimos cinco minutos, para el tablero técnico (letra, sección 6.12): tiempo medio,
// percentiles 95 y 99 y tasa de errores. Guarda como mucho 10.000 muestras; con más tráfico descarta las más viejas.
public sealed class VentanaLatencias(TimeProvider reloj)
{
    public static readonly TimeSpan Duracion = TimeSpan.FromMinutes(5);
    public const int MaximoMuestras = 10_000;

    private readonly ConcurrentQueue<Muestra> muestras = new();

    public void Registrar(double milisegundos, bool error)
    {
        muestras.Enqueue(new Muestra(reloj.GetUtcNow(), milisegundos, error));
        while (muestras.Count > MaximoMuestras && muestras.TryDequeue(out _))
        {
        }
    }

    public ResumenLatencias Resumir()
    {
        var desde = reloj.GetUtcNow() - Duracion;
        while (muestras.TryPeek(out var masVieja) && masVieja.Momento < desde && muestras.TryDequeue(out _))
        {
        }

        var vigentes = muestras.Where(m => m.Momento >= desde).ToArray();
        if (vigentes.Length == 0)
        {
            return ResumenLatencias.SinSolicitudes;
        }

        var duraciones = vigentes.Select(m => m.Milisegundos).Order().ToArray();
        return new ResumenLatencias(
            vigentes.Length,
            duraciones.Average(),
            Percentil(duraciones, 0.95),
            Percentil(duraciones, 0.99),
            100.0 * vigentes.Count(m => m.Error) / vigentes.Length);
    }

    // Método del rango más cercano: el menor valor que deja a su altura o por debajo el porcentaje pedido de las muestras.
    private static double Percentil(double[] ordenadas, double porcentaje) =>
        ordenadas[(int)Math.Ceiling(porcentaje * ordenadas.Length) - 1];

    private readonly record struct Muestra(DateTimeOffset Momento, double Milisegundos, bool Error);
}

public sealed record ResumenLatencias(int Solicitudes, double MediaMs, double P95Ms, double P99Ms, double PorcentajeErrores)
{
    public static readonly ResumenLatencias SinSolicitudes = new(0, 0, 0, 0, 0);
}

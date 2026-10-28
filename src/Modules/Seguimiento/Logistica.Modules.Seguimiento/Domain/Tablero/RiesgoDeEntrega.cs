namespace Logistica.Modules.Seguimiento.Domain.Tablero;

// Cuánto le queda a un envío no entregado hasta el fin de su ventana comprometida. Vencida: la ventana ya terminó.
internal sealed record EvaluacionRiesgo(DateTimeOffset FinVentana, TimeSpan Restante, bool EnRiesgo)
{
    public bool Vencida => Restante < TimeSpan.Zero;
}

// CU-66, requerimiento especial 3: un envío está en riesgo si no se entregó y le quedan menos de 60 minutos hasta el
// fin de su ventana (la fecha programada y el final de su franja, en la zona horaria del operador). El umbral es fijo y
// no se configura. Una ventana ya vencida también está en riesgo: es el caso más urgente.
internal static class RiesgoDeEntrega
{
    public static readonly TimeSpan Umbral = TimeSpan.FromMinutes(60);

    // Quien llama pasa sólo envíos con la entrega pendiente; los entregados, cancelados o extraviados no se evalúan.
    public static EvaluacionRiesgo Evaluar(DateOnly fecha, TimeOnly finFranja, TimeZoneInfo zonaOperador, DateTimeOffset ahora)
    {
        var finVentana = FinDeVentana(fecha, finFranja, zonaOperador);
        var restante = finVentana - ahora;
        return new EvaluacionRiesgo(finVentana, restante, restante < Umbral);
    }

    // La franja es hora local del operador: "hasta las 18:00 del 27/10" en Montevideo son las 21:00 UTC.
    public static DateTimeOffset FinDeVentana(DateOnly fecha, TimeOnly finFranja, TimeZoneInfo zonaOperador)
    {
        var local = fecha.ToDateTime(finFranja, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zonaOperador), TimeSpan.Zero);
    }
}

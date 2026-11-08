using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.CierresDiarios;

// CU-70, paso 6: el resultado del cierre de un día del operador. Hay uno solo por operador y día (índice único): que
// exista es lo que impide repetir el cierre (A1). Se registra al final, así un cierre que falló se reintenta (A2).
internal sealed class CierreDiario : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    // El día del operador, en su zona horaria.
    public DateOnly Fecha { get; private set; }
    public int EnviosDevueltos { get; private set; }
    public int RutasSinFinalizar { get; private set; }
    public int LiquidacionesGeneradas { get; private set; }
    public DateTimeOffset EjecutadoEn { get; private set; }

    private CierreDiario() { } // para EF Core

    public static CierreDiario Registrar(Guid operadorId, DateOnly fecha, int enviosDevueltos, int rutasSinFinalizar,
        int liquidacionesGeneradas, DateTimeOffset ejecutadoEn)
    {
        if (operadorId == Guid.Empty)
            throw new DomainException("El cierre necesita el operador.");
        if (enviosDevueltos < 0 || rutasSinFinalizar < 0 || liquidacionesGeneradas < 0)
            throw new DomainException("Las cantidades del cierre no pueden ser negativas.");
        return new CierreDiario
        {
            OperadorId = operadorId,
            Fecha = fecha,
            EnviosDevueltos = enviosDevueltos,
            RutasSinFinalizar = rutasSinFinalizar,
            LiquidacionesGeneradas = liquidacionesGeneradas,
            EjecutadoEn = ejecutadoEn.ToUniversalTime(),
        };
    }

    // Paso 4: el período de liquidación es el mes calendario; se liquida en el cierre de su último día.
    public static bool TerminaPeriodoDeLiquidacion(DateOnly fecha) => fecha.AddDays(1).Day == 1;

    public static DateOnly InicioPeriodoDeLiquidacion(DateOnly fecha) => new(fecha.Year, fecha.Month, 1);
}

using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.CierresDiarios;
using Logistica.Modules.Deposito.Contracts;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.EjecutarCierreDiario;

// La hora local a partir de la cual se cierra el día de cada operador (requerimiento especial 2).
internal sealed class OpcionesCierreDiario
{
    public TimeOnly Hora { get; set; } = new(23, 30);
}

// CU-70: el cierre del día del operador del ámbito. Cada paso lo ejecuta el módulo dueño, por su contrato, y es
// idempotente: repetirlo no devuelve dos veces un envío ni liquida dos veces un envío (A1). El resultado se registra
// al final: si un paso falla, no queda registrado y la próxima vuelta lo reintenta (A2). Los indicadores (paso 5) no se
// recalculan: CU-71 los actualiza con cada entrega e intento fallido.
internal sealed class EjecutarCierreDiarioHandler(IAdministracionModuleApi administracion, IEnviosModuleApi envios,
    IPlanificacionModuleApi planificacion, IDepositoModuleApi deposito, ICierresDiariosRepository cierres,
    ICurrentTenant tenant, TimeProvider reloj, OpcionesCierreDiario opciones)
{
    // null si todavía no es la hora del cierre o el día ya se cerró.
    public async Task<CierreDiario?> HandleAsync(CancellationToken ct)
    {
        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("El cierre necesita un operador.");
        var ahora = reloj.GetUtcNow();
        var zona = TimeZoneInfo.FindSystemTimeZoneById(await administracion.ObtenerZonaHorariaActualAsync(ct));
        var local = TimeZoneInfo.ConvertTime(ahora, zona);
        var hoy = DateOnly.FromDateTime(local.DateTime);
        if (TimeOnly.FromDateTime(local.DateTime) < opciones.Hora || await cierres.ExisteAsync(hoy, ct)) return null;

        // Paso 2 (T14).
        var devueltos = await envios.DevolverReprogramadosVencidosAsync(hoy, ct);
        // Paso 3.
        var rutas = await planificacion.AlertarRutasSinFinalizarAsync(hoy, ct);
        // Paso 4 (CU-32), sólo el último día del período.
        var liquidaciones = CierreDiario.TerminaPeriodoDeLiquidacion(hoy)
            ? await deposito.GenerarLiquidacionesDelPeriodoAsync(CierreDiario.InicioPeriodoDeLiquidacion(hoy), hoy, ct)
            : 0;

        // Paso 6.
        var cierre = CierreDiario.Registrar(operadorId, hoy, devueltos, rutas, liquidaciones, ahora);
        cierres.Agregar(cierre);
        await cierres.GuardarAsync(ct);
        return cierre;
    }
}

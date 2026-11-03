using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Deposito.Domain.Liquidaciones;
using Logistica.SharedKernel;

namespace Logistica.Modules.Deposito.Application.Features.ConsultarCuentaCorriente;

internal sealed record CuentaCorriente(decimal SaldoPendiente, IReadOnlyList<Liquidacion> Liquidaciones);

internal sealed record LineaCuentaCorriente(string NumeroEnvio, DateOnly Fecha, string Concepto, decimal Monto);

internal sealed record DetalleLiquidacion(Liquidacion Liquidacion, IReadOnlyList<LineaCuentaCorriente> Lineas);

// CU-33: el comercio consulta sus liquidaciones con el operador de la sesión. Los borradores no se muestran
// (requerimiento 1): el operador todavía los revisa y puede eliminarlos. Sólo lee.
internal sealed class ConsultarCuentaCorrienteHandler(ILiquidacionRepository liquidaciones,
    IAdministracionModuleApi administracion, ICurrentTenant tenant)
{
    // Paso 2. El saldo pendiente es lo emitido que todavía no se pagó.
    public async Task<CuentaCorriente> ConsultarAsync(CancellationToken ct)
    {
        var emitidas = await liquidaciones.ListarEmitidasAsync(ComercioId(), ct);
        var saldo = emitidas.Where(l => l.Estado == EstadoLiquidacion.Emitida).Sum(l => l.TotalNeto);
        return new CuentaCorriente(saldo, emitidas);
    }

    // Pasos 3 y 4. null si no existe, es de otro comercio u operador, o todavía es un borrador: el comercio no
    // distingue entre esos casos (404, ADR-0002).
    public async Task<DetalleLiquidacion?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var liquidacion = await liquidaciones.ObtenerAsync(id, ct);
        if (liquidacion is null || liquidacion.ComercioId != ComercioId() || liquidacion.Estado == EstadoLiquidacion.Borrador)
            return null;

        // La fecha de cada envío en días del operador, como el período de la liquidación (CU-32).
        var zona = TimeZoneInfo.FindSystemTimeZoneById(await administracion.ObtenerZonaHorariaActualAsync(ct));
        var lineas = liquidacion.Lineas
            .OrderBy(l => l.Fecha)
            .Select(l => new LineaCuentaCorriente(l.NumeroEnvio,
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(l.Fecha, zona).DateTime), l.Concepto, l.Monto))
            .ToList();
        return new DetalleLiquidacion(liquidacion, lineas);
    }

    // El filtro "Tenant" ya limita al comercio de la sesión; el chequeo explícito evita depender sólo de él.
    private Guid ComercioId() => tenant.ComercioId
        ?? throw new InvalidOperationException("La sesión no tiene un comercio.");
}

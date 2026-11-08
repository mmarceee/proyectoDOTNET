using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.Modules.Deposito.Domain.Liquidaciones;
using Logistica.Modules.Envios.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Deposito.Application.Features.GenerarLiquidacion;

internal sealed record GenerarLiquidacionCommand(Guid ComercioId, DateOnly Desde, DateOnly Hasta);

// CU-32: el administrador genera la liquidación de un comercio y un período, la revisa en borrador, la emite y registra
// el pago (A3). Los envíos liquidables los informa Envíos por su contrato, con la tarifa con que se admitieron: así la
// definición de "liquidable" vive con el envío, que es lo que cambia con la entrega parcial (RF 31 a 36).
internal sealed class GenerarLiquidacionHandler(IEnviosModuleApi envios, IAdministracionModuleApi administracion,
    ILiquidacionRepository liquidaciones, ICurrentTenant tenant, TimeProvider reloj)
{
    public Task<IReadOnlyList<ComercioRelacionado>> ComerciosAsync(CancellationToken ct) =>
        administracion.ConsultarComerciosAsync(ct);

    public Task<IReadOnlyList<Liquidacion>> ListarAsync(CancellationToken ct) => liquidaciones.ListarAsync(ct);

    public Task<Liquidacion?> ObtenerAsync(Guid id, CancellationToken ct) => liquidaciones.ObtenerAsync(id, ct);

    // CU-70, paso 4: la liquidación del período de cada comercio del operador, generada por el cierre diario. Saltea los
    // comercios sin envíos sin liquidar (A1), así repetirlo no genera otra liquidación con los mismos envíos (A2).
    public async Task<int> GenerarDelPeriodoAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var generadas = 0;
        foreach (var comercio in await administracion.ConsultarComerciosAsync(ct))
        {
            try
            {
                await GenerarAsync(new GenerarLiquidacionCommand(comercio.ComercioId, desde, hasta), ct);
                generadas++;
            }
            catch (DomainException)
            {
                // A1: el comercio no tiene envíos sin liquidar en el período.
            }
        }
        return generadas;
    }

    public async Task<Liquidacion> GenerarAsync(GenerarLiquidacionCommand command, CancellationToken ct)
    {
        var operadorId = tenant.OperadorId
            ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        if (command.Hasta < command.Desde)
            throw new DomainException("El período termina antes de empezar.");
        if ((await administracion.ConsultarComerciosAsync(ct)).All(c => c.ComercioId != command.ComercioId))
            throw new DomainException("Elegí un comercio que trabaje con el operador.");

        // El período son días del operador: de las 0 h del primero a las 0 h del día siguiente al último, en su zona.
        var zona = TimeZoneInfo.FindSystemTimeZoneById(await administracion.ObtenerZonaHorariaActualAsync(ct));
        var desde = Inicio(command.Desde, zona);
        var hasta = Inicio(command.Hasta.AddDays(1), zona);

        // Paso 2 y A2: los envíos que llegaron a un estado liquidable en el período y que no están en otra liquidación.
        var liquidables = await envios.ListarLiquidablesAsync(command.ComercioId, desde, hasta, ct);
        var yaLiquidados = await liquidaciones.EnviosLiquidadosAsync(liquidables.Select(e => e.EnvioId).ToList(), ct);
        var lineas = liquidables
            .Where(e => !yaLiquidados.Contains(e.EnvioId))
            .Select(e => new DatosLinea(e.EnvioId, e.Numero, e.LlegoEn,
                e.Estado == "Devuelto" ? "Envío devuelto" : "Envío entregado", e.MontoTarifa))
            .ToList();

        var liquidacion = Liquidacion.Generar(operadorId, command.ComercioId, command.Desde, command.Hasta, lineas,
            reloj.GetUtcNow());
        liquidaciones.Agregar(liquidacion);
        await liquidaciones.GuardarCambiosAsync(ct);
        return liquidacion;
    }

    // Paso 6. false si la liquidación no existe o es de otro operador.
    public Task<bool> EmitirAsync(Guid id, CancellationToken ct) =>
        CambiarAsync(id, l => l.Emitir(reloj.GetUtcNow()), ct);

    // A3. false si la liquidación no existe o es de otro operador.
    public Task<bool> MarcarPagadaAsync(Guid id, CancellationToken ct) =>
        CambiarAsync(id, l => l.MarcarPagada(reloj.GetUtcNow()), ct);

    // Un borrador se elimina para volver a generarlo. Si otro administrador lo emitió mientras tanto, la concurrencia
    // optimista rechaza el borrado. false si la liquidación no existe o es de otro operador.
    public Task<bool> EliminarAsync(Guid id, CancellationToken ct) =>
        CambiarAsync(id, l =>
        {
            l.ValidarEliminacion();
            liquidaciones.Eliminar(l);
        }, ct);

    private async Task<bool> CambiarAsync(Guid id, Action<Liquidacion> cambio, CancellationToken ct)
    {
        if (await liquidaciones.ObtenerAsync(id, ct) is not { } liquidacion)
            return false;
        cambio(liquidacion);
        await liquidaciones.GuardarCambiosAsync(ct);
        return true;
    }

    private static DateTimeOffset Inicio(DateOnly dia, TimeZoneInfo zona) =>
        new(TimeZoneInfo.ConvertTimeToUtc(dia.ToDateTime(TimeOnly.MinValue), zona), TimeSpan.Zero);
}

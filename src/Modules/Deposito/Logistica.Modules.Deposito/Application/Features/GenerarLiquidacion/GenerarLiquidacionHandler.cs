using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.Modules.Deposito.Domain.Liquidaciones;
using Logistica.Modules.Envios.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Deposito.Application.Features.GenerarLiquidacion;

internal sealed record GenerarLiquidacionCommand(Guid ComercioId, DateOnly Desde, DateOnly Hasta);

// CU-32: el administrador genera la liquidación de un comercio y un período, la revisa en borrador, la emite y registra
// el pago (A3). Los envíos liquidables y los recargos los informa Envíos por su contrato, con los montos con que se
// registraron: así la definición de "liquidable" vive con el envío, que es lo que cambia con la entrega parcial (RF 31 a
// 36; cierre técnico, D8). Depósito no recalcula tarifas.
internal sealed class GenerarLiquidacionHandler(IEnviosModuleApi envios, IAdministracionModuleApi administracion,
    ILiquidacionRepository liquidaciones, ICurrentTenant tenant, TimeProvider reloj)
{
    public Task<IReadOnlyList<ComercioRelacionado>> ComerciosAsync(CancellationToken ct) =>
        administracion.ConsultarComerciosAsync(ct);

    public Task<IReadOnlyList<Liquidacion>> ListarAsync(CancellationToken ct) => liquidaciones.ListarAsync(ct);

    public Task<Liquidacion?> ObtenerAsync(Guid id, CancellationToken ct) => liquidaciones.ObtenerAsync(id, ct);

    // CU-70, paso 4: la liquidación del período de cada comercio del operador, generada por el cierre diario. Saltea los
    // comercios sin envíos ni recargos sin liquidar (A1), así repetirlo no genera otra liquidación con lo mismo (A2).
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

        // Paso 2 y A2 (cierre técnico, D8): la tarifa base de los envíos que terminaron en el período y los recargos por
        // nueva visita registrados en el período, que no estén en otra liquidación. Un recargo se cobra por la fecha de
        // su visita, aunque el envío termine en otro período.
        var liquidables = await envios.ListarLiquidablesAsync(command.ComercioId, desde, hasta, ct);
        var recargos = await envios.ListarRecargosAsync(command.ComercioId, desde, hasta, ct);
        var basesCobradas = await liquidaciones.ConceptosLiquidadosAsync(OrigenLiquidacion.BaseEnvio,
            liquidables.Select(e => e.EnvioId).ToList(), ct);
        var recargosCobrados = await liquidaciones.ConceptosLiquidadosAsync(OrigenLiquidacion.RecargoVisita,
            recargos.Select(r => r.RecargoId).ToList(), ct);

        var lineas = liquidables
            .Where(e => !basesCobradas.Contains(e.EnvioId))
            .Select(e => new DatosLinea(e.EnvioId, e.Numero, e.LlegoEn, ConceptoBase(e.Estado), e.MontoTarifa))
            .Concat(recargos
                .Where(r => !recargosCobrados.Contains(r.RecargoId))
                .Select(r => new DatosLinea(r.EnvioId, r.Numero, r.OcurridoEn, "Recargo por nueva visita", r.Monto,
                    OrigenLiquidacion.RecargoVisita, r.RecargoId)))
            .OrderBy(l => l.Fecha)
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

    // Cómo ve el comercio la tarifa base según el cierre del envío. El cierre mixto cobra la tarifa de admisión entera,
    // una vez, sin prorratearla por bulto (D8).
    private static string ConceptoBase(string cierre) => cierre switch
    {
        "Devuelto" => "Envío devuelto",
        "EntregadoParcialmente" => "Envío entregado parcialmente",
        _ => "Envío entregado",
    };

    private static DateTimeOffset Inicio(DateOnly dia, TimeZoneInfo zona) =>
        new(TimeZoneInfo.ConvertTimeToUtc(dia.ToDateTime(TimeOnly.MinValue), zona), TimeSpan.Zero);
}

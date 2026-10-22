using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Ejecucion.Application.Features.ReportarPosicion;
using Logistica.Modules.Ejecucion.Domain.Posiciones;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Logistica.Modules.Ejecucion.Infrastructure.Persistence;

internal sealed class DepurarPosiciones(IServiceScopeFactory ambitos, IOptions<RetencionPosiciones> opciones,
    TimeProvider reloj, ILogger<DepurarPosiciones> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await DepurarAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                { log.LogWarning(ex, "No se pudo depurar el historial de posiciones; se reintentará en una hora"); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    internal async Task DepurarAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> operadores;
        await using (var listado = ambitos.CreateAsyncScope())
            operadores = await listado.ServiceProvider.GetRequiredService<IAdministracionModuleApi>().ConsultarOperadoresActivosAsync(ct);
        var desde = reloj.GetUtcNow().AddDays(-opciones.Value.Dias);
        foreach (var operador in operadores)
        {
            try
            {
                // ADR-0002 §2.4: fijar tenant antes de resolver repositorios y DbContext.
                await using var ambito = ambitos.CreateAsyncScope();
                ambito.ServiceProvider.GetRequiredService<InquilinoTrabajo>().Fijar(operador);
                await ambito.ServiceProvider.GetRequiredService<IPosicionRepository>().EliminarAnterioresAsync(desde, ct);
                // ADR-0004 §2.5 y §6: no borrar evidencia de rechazos pendientes de revisión.
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { log.LogWarning(ex, "No se pudieron depurar las posiciones del operador {OperadorId}", operador); }
        }
    }
}

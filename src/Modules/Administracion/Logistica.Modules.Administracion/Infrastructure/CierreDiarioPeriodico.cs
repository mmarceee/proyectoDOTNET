using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Administracion.Application.Features.EjecutarCierreDiario;
using Logistica.Modules.Administracion.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Logistica.Modules.Administracion.Infrastructure;

// CU-70, paso 1: cada 5 minutos revisa a cada operador activo y cierra su día cuando llega la hora en su zona horaria.
// Corre en la API, como DepurarPosiciones: un ámbito por operador con InquilinoTrabajo (ADR-0002, sección 2.4). Si un
// operador falla, los demás siguen y el suyo se reintenta en la vuelta siguiente (A2).
internal sealed class CierreDiarioPeriodico(IServiceScopeFactory ambitos, ILogger<CierreDiarioPeriodico> log)
    : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await EjecutarAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                { log.LogWarning(ex, "No se pudo revisar el cierre diario; se reintentará en {Minutos} minutos", Intervalo.TotalMinutes); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    internal async Task EjecutarAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> operadores;
        await using (var listado = ambitos.CreateAsyncScope())
            operadores = await listado.ServiceProvider.GetRequiredService<IAdministracionModuleApi>().ConsultarOperadoresActivosAsync(ct);
        foreach (var operador in operadores)
        {
            try
            {
                // Fijar el inquilino antes de resolver repositorios y DbContext.
                await using var ambito = ambitos.CreateAsyncScope();
                ambito.ServiceProvider.GetRequiredService<InquilinoTrabajo>().Fijar(operador);
                var cierre = await ambito.ServiceProvider.GetRequiredService<EjecutarCierreDiarioHandler>().HandleAsync(ct);
                if (cierre is not null)
                {
                    log.LogInformation(
                        "Cierre del {Fecha} del operador {OperadorId}: {Devueltos} envíos devueltos, {Rutas} rutas sin finalizar, {Liquidaciones} liquidaciones",
                        cierre.Fecha, operador, cierre.EnviosDevueltos, cierre.RutasSinFinalizar, cierre.LiquidacionesGeneradas);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { log.LogWarning(ex, "No se pudo cerrar el día del operador {OperadorId}; se reintentará", operador); }
        }
    }
}

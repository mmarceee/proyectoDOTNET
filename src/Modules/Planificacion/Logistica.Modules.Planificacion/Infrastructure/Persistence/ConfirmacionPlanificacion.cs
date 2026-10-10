using Logistica.Modules.Planificacion.Application.Abstractions;
using Logistica.Modules.Planificacion.Application.Exceptions;
using Logistica.Modules.Envios.Contracts;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence;

internal sealed class ConfirmacionPlanificacion(IUnidadDeTrabajo unidad, IServiceScopeFactory scopes) : IConfirmacionPlanificacion
{
    public async Task EjecutarAsync(Func<Task> trabajo, IReadOnlyCollection<Guid> envioIds, CancellationToken ct)
    {
        try { await unidad.EjecutarAsync(trabajo, ct); }
        catch (DbUpdateConcurrencyException)
        {
            var indisponibles = await IndisponiblesAsync(envioIds, ct);
            throw new ConflictoRutaException(indisponibles.Count > 0 ? "EnvioNoDisponible" : "RutaModificada",
                "Otra operación modificó los datos. Revisá y confirmá nuevamente.", indisponibles);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23505", ConstraintName: "UX_Paradas_EnvioActivo" })
        {
            throw new ConflictoRutaException("EnvioNoDisponible", "Algunos envíos fueron asignados por otra operación.", await IndisponiblesAsync(envioIds, ct));
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23505", ConstraintName: "UX_Rutas_VehiculoReserva" or "UX_Rutas_RepartidorReserva" } pg)
        {
            var codigo = pg.ConstraintName == "UX_Rutas_VehiculoReserva" ? "VehiculoOcupado" : "RepartidorOcupado";
            throw new ConflictoRutaException(codigo, "Un recurso dejó de estar disponible.", recursos: [codigo]);
        }
    }
    private async Task<IReadOnlyList<Guid>> IndisponiblesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var datos = await scope.ServiceProvider.GetRequiredService<IEnviosModuleApi>().ObtenerParaPlanificacionAsync(ids, ct);
        return ids.Where(id => !datos.Any(e => e.Id == id && e.Estado is "EnDeposito" or "Reprogramado")).ToList();
    }
}

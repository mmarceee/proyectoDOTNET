using Logistica.Modules.Administracion.Domain.Planificacion;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

// CU-08. No filtra por operador a mano: el filtro "Tenant" de ModuleDbContext ya limita todo al de la sesión.
// Las consultas tienen seguimiento de cambios, porque el handler modifica lo que trae y después guarda.
// Cada lectura se suma a la transacción en curso: la desactivación lee y guarda con el bloqueo de planificación tomado.
internal sealed class FlotaRepository(AdministracionDbContext db) : IFlotaRepository
{
    public async Task<IReadOnlyList<Repartidor>> ListarRepartidoresAsync(CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Repartidores.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Vehiculo>> ListarVehiculosAsync(CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Vehiculos.ToListAsync(ct);
    }

    public async Task<Repartidor?> ObtenerRepartidorAsync(Guid id, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Repartidores.SingleOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<Vehiculo?> ObtenerVehiculoAsync(Guid id, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Vehiculos.SingleOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task<bool> ExisteDocumentoAsync(string documento, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Repartidores.AnyAsync(r => r.Documento == documento, ct);
    }

    public async Task<bool> ExisteMatriculaAsync(string matricula, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        return await db.Vehiculos.AnyAsync(v => v.Matricula == matricula, ct);
    }

    public void Agregar(Repartidor repartidor)
    {
        db.Repartidores.Add(repartidor);
    }

    public void Agregar(Vehiculo vehiculo)
    {
        db.Vehiculos.Add(vehiculo);
    }

    // A1 con dos altas simultáneas: las dos pasan la consulta previa, y el índice único frena a la segunda. Se informa
    // igual que la consulta previa, en lugar de un error 500.
    public async Task GuardarCambiosAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Repartidores_OperadorId_Documento" })
        {
            throw new DomainException("Ya existe un repartidor con ese documento.");
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Vehiculos_OperadorId_Matricula" })
        {
            throw new DomainException("Ya existe un vehículo con esa matrícula.");
        }
    }
}

using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Logistica.BuildingBlocks.Infrastructure.Persistence;

// Cierra del lado de las escrituras lo que el filtro "Tenant" cierra del lado de las lecturas (ADR-0002, sección 2.3).
// No cubre ExecuteUpdate, ExecuteDelete ni el SQL crudo: no pasan por el change tracker (sección 4).
// No guarda estado: lee el inquilino del contexto que está guardando, así una sola instancia sirve para todos.
internal sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Validar(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Validar(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Validar(DbContext? context)
    {
        if (context is not ModuleDbContext db)
        {
            return;
        }

        var tenant = db.Tenant;

        // Entries() corre DetectChanges antes: incluye lo agregado por navegación (los bultos de un envío)
        // y lo modificado sin llamar a Update.
        foreach (var entry in db.ChangeTracker.Entries<IOperadorOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            // Falla cerrado: sin operador en la sesión no se escribe ninguna entidad de un operador.
            if (tenant.OperadorId is not Guid operadorId)
            {
                throw new TenantMismatchException(
                    $"Se intentó guardar {entry.Metadata.ClrType.Name} sin un operador en la sesión.");
            }

            ValidarMarcador(entry, nameof(IOperadorOwned.OperadorId), operadorId);

            // Personal del operador (sin comercio en la sesión): el interceptor no valida el comercio;
            // que tenga relación activa con el operador es una regla del caso de uso (sección 2.3).
            if (entry.Entity is IComercioOwned && tenant.ComercioId is Guid comercioId)
            {
                ValidarMarcador(entry, nameof(IComercioOwned.ComercioId), comercioId);
            }
        }
    }

    private static void ValidarMarcador(EntityEntry entry, string propiedad, Guid esperado)
    {
        var valor = entry.Property(propiedad);
        var actual = (Guid)valor.CurrentValue!;

        // Alta sin el marcador: se asigna desde la sesión, así el handler no tiene que hacerlo.
        if (entry.State == EntityState.Added && actual == Guid.Empty)
        {
            valor.CurrentValue = esperado;
            return;
        }

        // Una entidad no cambia de inquilino una vez creada.
        if (entry.State == EntityState.Modified && (Guid)valor.OriginalValue! != actual)
        {
            throw new TenantMismatchException(
                $"Se intentó cambiar {entry.Metadata.ClrType.Name}.{propiedad} de {valor.OriginalValue} a {actual}.");
        }

        if (actual != esperado)
        {
            throw new TenantMismatchException(
                $"Se intentó guardar {entry.Metadata.ClrType.Name} con {propiedad} {actual}; la sesión es de {esperado}.");
        }
    }
}

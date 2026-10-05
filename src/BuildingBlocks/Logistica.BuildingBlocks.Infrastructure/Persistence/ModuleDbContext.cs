using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.BuildingBlocks.Infrastructure.Persistence;

// Base de los DbContext de los módulos: cada módulo en su propio schema de PostgreSQL (ADR-0003).
public abstract class ModuleDbContext(DbContextOptions options, string schema) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(schema);

        // Toma las clases IEntityTypeConfiguration del módulo: hay un DbContext por módulo.
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            var entity = modelBuilder.Entity(entityType.ClrType);

            // El Id lo genera el dominio (Guid v7): EF no debe tratarlo como generado por la base.
            entity.Property(nameof(Entity.Id)).ValueGeneratedNever();

            // Los eventos de dominio no se guardan en la tabla.
            entity.Ignore(nameof(Entity.DomainEvents));
        }
    }
}

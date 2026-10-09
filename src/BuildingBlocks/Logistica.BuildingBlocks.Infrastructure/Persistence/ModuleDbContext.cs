using System.Data.Common;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.BuildingBlocks.Infrastructure.Persistence;

// Base de los DbContext de los módulos: cada módulo en su propio schema de PostgreSQL (ADR-0003),
// todos sobre la conexión del request y sumándose a la transacción de la unidad de trabajo si hay una.
public abstract class ModuleDbContext(DbContextOptions options, string schema, UnidadDeTrabajo unidadDeTrabajo)
    : DbContext(options)
{
    // La transacción compartida a la que este DbContext está sumado, para no volver a sumarlo.
    private DbTransaction? _transaccionCompartida;

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await SumarseATransaccionCompartidaAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SumarseATransaccionCompartidaAsync(CancellationToken.None).GetAwaiter().GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    // Dentro de una unidad de trabajo, EF no abre su propia transacción: usa la compartida.
    // Cuando la unidad termina, se desengancha (UseTransaction(null)) para que los próximos
    // guardados vuelvan a tener su transacción propia.
    public Task ParticiparEnTransaccionAsync(CancellationToken ct) => SumarseATransaccionCompartidaAsync(ct);

    private async Task SumarseATransaccionCompartidaAsync(CancellationToken ct)
    {
        var compartida = unidadDeTrabajo.Transaccion;

        if (compartida == _transaccionCompartida)
        {
            return;
        }

        await Database.UseTransactionAsync(compartida, ct);
        _transaccionCompartida = compartida;
    }

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

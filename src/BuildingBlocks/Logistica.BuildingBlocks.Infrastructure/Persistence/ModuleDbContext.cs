using System.Reflection;
using System.Data.Common;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.BuildingBlocks.Infrastructure.Persistence;

// Base de los DbContext de los módulos: cada módulo en su propio schema de PostgreSQL (ADR-0003),
// todos sobre la conexión del request y sumándose a la transacción de la unidad de trabajo si hay una.
public abstract class ModuleDbContext(DbContextOptions options, string schema, UnidadDeTrabajo unidadDeTrabajo, ICurrentTenant tenant)
    : DbContext(options)
{
    // La transacción compartida a la que este DbContext está sumado, para no volver a sumarlo.
    private DbTransaction? _transaccionCompartida;

    // Nombre del filtro global de inquilino (ADR-0002). Se usa en IgnoreQueryFilters([FiltroTenant]).
    public const string FiltroTenant = "Tenant";

    // El filtro lee estas propiedades del contexto que ejecuta la consulta, no del que construyó
    // el modelo: EF construye el modelo una sola vez y lo reutiliza en todos los contextos.
    private Guid? OperadorIdActual => tenant.OperadorId;
    private Guid? ComercioIdActual => tenant.ComercioId;

    // Una sola instancia para todos los contextos: el interceptor no guarda estado. Si cada contexto
    // registrara la suya, EF armaría un proveedor de servicios interno por contexto.
    private static readonly TenantSaveChangesInterceptor InterceptorTenant = new();

    // Lo lee TenantSaveChangesInterceptor para validar las escrituras.
    internal ICurrentTenant Tenant => tenant;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(InterceptorTenant);
    }

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

        // Los marcadores también se aplican a filas de infraestructura que no heredan Entity, como OutboxMessage.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(IOperadorOwned).IsAssignableFrom(t.ClrType)))
        {
            // Filtro "Tenant": las entidades del comercio se filtran por operador y comercio;
            // las del operador, solo por operador.
            if (typeof(IComercioOwned).IsAssignableFrom(entityType.ClrType))
            {
                AplicarFiltro(nameof(AplicarFiltroComercio), entityType.ClrType, modelBuilder);
            }
            else if (typeof(IOperadorOwned).IsAssignableFrom(entityType.ClrType))
            {
                AplicarFiltro(nameof(AplicarFiltroOperador), entityType.ClrType, modelBuilder);
            }
        }
    }

    // El tipo de la entidad se conoce recien en ejecucion: se llama al metodo generico de este tipo.
    private void AplicarFiltro(string metodo, Type tipoEntidad, ModelBuilder modelBuilder)
    {
        typeof(ModuleDbContext)
            .GetMethod(metodo, BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(tipoEntidad)
            .Invoke(this, [modelBuilder]);
    }

    // Sin operador en la sesion no se ve nada (falla cerrado).
    private void AplicarFiltroOperador<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IOperadorOwned
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(FiltroTenant,
            e => OperadorIdActual != null && e.OperadorId == OperadorIdActual);
    }

    private void AplicarFiltroComercio<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IOperadorOwned, IComercioOwned
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(FiltroTenant,
            e => OperadorIdActual != null && e.OperadorId == OperadorIdActual &&
                (ComercioIdActual == null || e.ComercioId == ComercioIdActual));
    }
}

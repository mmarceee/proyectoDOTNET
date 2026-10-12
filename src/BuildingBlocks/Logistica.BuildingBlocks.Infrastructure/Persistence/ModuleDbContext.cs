using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
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

    // La tabla outbox del módulo (ADR-0003, sección 2.1). Sin filtro "Tenant": la lee el publicador,
    // que corre sin inquilino. Para escribir en ella, AgregarAlOutbox.
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    // Registra un evento de integración en el outbox del módulo; se confirma con el próximo SaveChanges,
    // junto con el cambio que lo origina, o se revierte con él (ADR-0003, sección 2.2). El inquilino del
    // sobre sale de la sesión y nunca del llamador (ADR-0002, sección 2.5); sin operador, falla cerrado.
    public void AgregarAlOutbox<T>(string tipo, T contenido, DateTimeOffset ocurridoEn)
    {
        var operadorId = tenant.OperadorId
            ?? throw new TenantMismatchException($"Se intentó registrar {tipo} en el outbox sin un operador en la sesión.");

        Outbox.Add(OutboxMessage.Crear(tipo, contenido, ocurridoEn, operadorId, tenant.ComercioId, Activity.Current?.Id));
    }

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

        ConfigurarOutbox(modelBuilder);

        // Recorre todos los tipos con el marcador, hereden o no de Entity. El outbox no lo tiene, así que
        // queda sin filtro, como pide el ADR-0002 (sección 2.4).
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

    // Una tabla outbox_messages por módulo, en el schema del módulo (HasDefaultSchema).
    private static void ConfigurarOutbox(ModelBuilder modelBuilder)
    {
        var outbox = modelBuilder.Entity<OutboxMessage>();
        outbox.ToTable("outbox_messages");
        outbox.HasKey(m => m.Id);
        outbox.Property(m => m.Id).ValueGeneratedNever();
        outbox.Property(m => m.Tipo).HasMaxLength(200);
        outbox.Property(m => m.TraceParent).HasMaxLength(55);

        // El publicador busca los pendientes por orden de llegada: el índice parcial sólo guarda esos.
        outbox.HasIndex(m => m.OcurridoEn).HasFilter("\"PublicadoEn\" IS NULL");
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

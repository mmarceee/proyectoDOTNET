using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Domain.Incidencias;
using Logistica.Modules.Envios.Domain.Devoluciones;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

internal sealed class EnviosDbContext(DbContextOptions<EnviosDbContext> options, UnidadDeTrabajo unidadDeTrabajo, ICurrentTenant tenant)
    : ModuleDbContext(options, Schema, unidadDeTrabajo, tenant)
{
    public const string Schema = "envios";

    public DbSet<Envio> Envios => Set<Envio>();
    public DbSet<IntentoEntrega> IntentosEntrega => Set<IntentoEntrega>();
    public DbSet<Incidencia> Incidencias => Set<Incidencia>();
    public DbSet<Devolucion> Devoluciones => Set<Devolucion>();
    public DbSet<ArchivoEvidencia> ArchivosEvidencia => Set<ArchivoEvidencia>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<OutboxMessage>().ToTable("outbox_messages");
        modelBuilder.Entity<OutboxMessage>().Property(m => m.Id).ValueGeneratedNever();
        modelBuilder.Entity<OutboxMessage>().HasIndex(m => new { m.PublicadoEn, m.OcurridoEn });

        // De acá sale el número de cada envío: único entre todos los operadores y, por lo tanto, en cada uno.
        modelBuilder.HasSequence<long>("numero_envio");
    }
}

using Logistica.Modules.Administracion.Domain.Tarifas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Configurations;

internal sealed class VersionTarifarioConfiguration : IEntityTypeConfiguration<VersionTarifario>
{
    public void Configure(EntityTypeBuilder<VersionTarifario> builder)
    {
        builder.ToTable("VersionesTarifario");
        builder.Property(v => v.Estado).HasConversion<string>();

        // A5: concurrencia optimista sobre xmin, la versión de fila de PostgreSQL. Si dos administradores publican el
        // mismo borrador, el segundo guardado falla y se le pide revisar.
        builder.Property<uint>("Version").IsRowVersion();

        // El número es único por operador (ADR-0002, sección 2.1).
        builder.HasIndex(v => new { v.OperadorId, v.Numero }).IsUnique();

        // A4: a lo sumo un borrador por operador. Índice único parcial: sólo cuenta las filas en Borrador.
        builder.HasIndex(v => v.OperadorId).IsUnique()
            .HasFilter("\"Estado\" = 'Borrador'")
            .HasDatabaseName("UX_VersionesTarifario_Borrador");

        builder.HasMany(v => v.Reglas).WithOne().HasForeignKey(r => r.VersionTarifarioId);
        builder.HasMany(v => v.Ajustes).WithOne().HasForeignKey(a => a.VersionTarifarioId);
    }
}

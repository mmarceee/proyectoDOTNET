using Logistica.Modules.Planificacion.Domain.Rutas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence.Configurations;

internal sealed class EnvioParadaConfiguration : IEntityTypeConfiguration<EnvioParada>
{
    public void Configure(EntityTypeBuilder<EnvioParada> b)
    {
        b.ToTable("EnviosParada"); b.Property(e => e.Estado).HasConversion<string>();
        b.HasIndex(e => e.EnvioId).IsUnique().HasFilter("\"Estado\" = 'Pendiente'").HasDatabaseName("UX_Paradas_EnvioActivo");
        b.HasIndex(e => new { e.RutaId, e.EnvioId }).IsUnique();
    }
}

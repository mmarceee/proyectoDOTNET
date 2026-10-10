using Logistica.Modules.Planificacion.Domain.Rutas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence.Configurations;

internal sealed class ParadaConfiguration : IEntityTypeConfiguration<Parada>
{
    public void Configure(EntityTypeBuilder<Parada> b)
    {
        b.ToTable("Paradas"); b.Property(p => p.Estado).HasConversion<string>();
        b.HasIndex(p => p.EnvioId).IsUnique().HasFilter("\"Estado\" = 'Pendiente'").HasDatabaseName("UX_Paradas_EnvioActivo");
        b.HasIndex(p => new { p.RutaId, p.EnvioId }).IsUnique();
    }
}

using Logistica.Modules.Planificacion.Domain.Rutas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence.Configurations;

internal sealed class ParadaConfiguration : IEntityTypeConfiguration<Parada>
{
    public void Configure(EntityTypeBuilder<Parada> b)
    {
        b.ToTable("Paradas"); b.Property(p => p.Estado).HasConversion<string>();
        b.HasAlternateKey(p => new { p.OperadorId, p.RutaId, p.Id });
        b.HasMany(p => p.Envios).WithOne().HasForeignKey(e => new { e.OperadorId, e.RutaId, e.ParadaId })
            .HasPrincipalKey(p => new { p.OperadorId, p.RutaId, p.Id });
    }
}

using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Configurations;

internal sealed class MovimientoCustodiaConfiguration : IEntityTypeConfiguration<MovimientoCustodia>
{
    public void Configure(EntityTypeBuilder<MovimientoCustodia> b)
    {
        b.ToTable("MovimientosCustodia");
        b.Property(m => m.Ubicacion).HasConversion<string>();
        b.Property(m => m.Motivo).HasMaxLength(500);
        b.HasIndex(m => new { m.OperadorId, m.BultoId, m.OperacionId }).IsUnique();
    }
}

using Logistica.Modules.Planificacion.Domain.Rutas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence.Configurations;

internal sealed class OperacionBultosConfiguration : IEntityTypeConfiguration<OperacionBultos>
{
    public void Configure(EntityTypeBuilder<OperacionBultos> b)
    {
        b.ToTable("OperacionesBultos");
        b.HasIndex(o => new { o.OperadorId, o.OperacionId }).IsUnique();
        b.Property(o => o.Huella).HasMaxLength(64);
    }
}

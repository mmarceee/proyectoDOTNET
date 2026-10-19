using Logistica.Modules.Ejecucion.Domain.Rendiciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Ejecucion.Infrastructure.Persistence.Configurations;

internal sealed class RendicionConfiguration : IEntityTypeConfiguration<Rendicion>
{
    public void Configure(EntityTypeBuilder<Rendicion> b)
    {
        b.ToTable("rendiciones");
        b.HasKey(r => r.Id);
        b.HasIndex(r => new { r.OperadorId, r.RutaId }).IsUnique();
        b.HasIndex(r => new { r.OperadorId, r.OperacionId }).IsUnique();
        b.Property(r => r.Observaciones).HasMaxLength(2000);
        b.HasMany(r => r.Lineas).WithOne().HasForeignKey(l => l.RendicionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(r => r.Lineas).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
internal sealed class LineaRendicionConfiguration : IEntityTypeConfiguration<LineaRendicion>
{
    public void Configure(EntityTypeBuilder<LineaRendicion> b)
    {
        b.ToTable("lineas_rendicion");
        b.HasKey(l => l.Id);
        b.HasIndex(l => new { l.OperadorId, l.RendicionId, l.BultoId }).IsUnique();
        b.Property(l => l.Situacion).HasMaxLength(30);
        b.Property(l => l.Observacion).HasMaxLength(1000);
        b.Property(l => l.EstadoFinal).HasConversion<string>().HasMaxLength(30);
    }
}

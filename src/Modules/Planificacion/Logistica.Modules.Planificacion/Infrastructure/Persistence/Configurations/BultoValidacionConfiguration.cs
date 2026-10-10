using Logistica.Modules.Planificacion.Domain.Rutas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence.Configurations;

internal sealed class BultoValidacionConfiguration : IEntityTypeConfiguration<BultoValidacionRuta>
{
    public void Configure(EntityTypeBuilder<BultoValidacionRuta> b)
    { b.ToTable("BultosValidacionRuta"); b.HasIndex(d => new { d.ValidacionRutaId, d.BultoId }).IsUnique(); }
}

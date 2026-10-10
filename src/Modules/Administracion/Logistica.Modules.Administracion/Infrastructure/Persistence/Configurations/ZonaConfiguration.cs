using Logistica.Modules.Administracion.Domain.Planificacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Configurations;

internal sealed class ZonaConfiguration : IEntityTypeConfiguration<Zona>
{
    public void Configure(EntityTypeBuilder<Zona> b)
    { b.ToTable("Zonas"); b.HasIndex(e => new { e.OperadorId, e.Codigo }).IsUnique(); }
}

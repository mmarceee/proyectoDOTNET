using Logistica.Modules.Administracion.Domain.Planificacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Configurations;

internal sealed class ReglasConfiguration : IEntityTypeConfiguration<VersionReglasPlanificacion>
{
    public void Configure(EntityTypeBuilder<VersionReglasPlanificacion> b)
    { b.ToTable("VersionesReglasPlanificacion"); b.HasIndex(e => new { e.OperadorId, e.Numero }).IsUnique(); }
}

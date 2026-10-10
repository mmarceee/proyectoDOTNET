using Logistica.Modules.Administracion.Domain.Planificacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Configurations;

internal sealed class VehiculoConfiguration : IEntityTypeConfiguration<Vehiculo>
{
    public void Configure(EntityTypeBuilder<Vehiculo> b)
    { b.ToTable("Vehiculos"); b.HasIndex(e => new { e.OperadorId, e.Matricula }).IsUnique(); }
}

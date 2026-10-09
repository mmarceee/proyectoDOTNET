using Logistica.Modules.Administracion.Domain.Planificacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Configurations;

internal sealed class RepartidorConfiguration : IEntityTypeConfiguration<Repartidor>
{
    public void Configure(EntityTypeBuilder<Repartidor> b)
    { b.ToTable("Repartidores"); b.HasIndex(e => new { e.OperadorId, e.Documento }).IsUnique(); }
}
internal sealed class VehiculoConfiguration : IEntityTypeConfiguration<Vehiculo>
{
    public void Configure(EntityTypeBuilder<Vehiculo> b)
    { b.ToTable("Vehiculos"); b.HasIndex(e => new { e.OperadorId, e.Matricula }).IsUnique(); }
}
internal sealed class ZonaConfiguration : IEntityTypeConfiguration<Zona>
{
    public void Configure(EntityTypeBuilder<Zona> b)
    { b.ToTable("Zonas"); b.HasIndex(e => new { e.OperadorId, e.Codigo }).IsUnique(); }
}
internal sealed class FranjaConfiguration : IEntityTypeConfiguration<FranjaHoraria>
{
    public void Configure(EntityTypeBuilder<FranjaHoraria> b)
    {
        b.ToTable("FranjasHorarias"); b.HasIndex(e => new { e.OperadorId, e.ZonaId, e.VigenteDesde });
        b.HasIndex(e => e.ReemplazaAId).IsUnique();
    }
}
internal sealed class ReglasConfiguration : IEntityTypeConfiguration<VersionReglasPlanificacion>
{
    public void Configure(EntityTypeBuilder<VersionReglasPlanificacion> b)
    { b.ToTable("VersionesReglasPlanificacion"); b.HasIndex(e => new { e.OperadorId, e.Numero }).IsUnique(); }
}

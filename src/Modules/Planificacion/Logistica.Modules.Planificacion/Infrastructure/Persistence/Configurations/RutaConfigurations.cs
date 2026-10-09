using Logistica.Modules.Planificacion.Domain.Rutas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence.Configurations;

internal sealed class RutaConfiguration : IEntityTypeConfiguration<Ruta>
{
    public void Configure(EntityTypeBuilder<Ruta> b)
    {
        b.ToTable("Rutas"); b.Property(r => r.Estado).HasConversion<string>(); b.Property(r => r.Revision).IsConcurrencyToken();
        b.HasAlternateKey(r => new { r.OperadorId, r.Id });
        b.HasIndex(r => new { r.OperadorId, r.Fecha, r.RepartidorId }).IsUnique().HasFilter("\"ReservaActiva\" = true").HasDatabaseName("UX_Rutas_RepartidorReserva");
        b.HasIndex(r => new { r.OperadorId, r.Fecha, r.VehiculoId }).IsUnique().HasFilter("\"ReservaActiva\" = true").HasDatabaseName("UX_Rutas_VehiculoReserva");
        b.HasMany(r => r.Paradas).WithOne().HasForeignKey(p => new { p.OperadorId, p.RutaId }).HasPrincipalKey(r => new { r.OperadorId, r.Id });
    }
}
internal sealed class ParadaConfiguration : IEntityTypeConfiguration<Parada>
{
    public void Configure(EntityTypeBuilder<Parada> b)
    {
        b.ToTable("Paradas"); b.Property(p => p.Estado).HasConversion<string>();
        b.HasIndex(p => p.EnvioId).IsUnique().HasFilter("\"Estado\" = 'Pendiente'").HasDatabaseName("UX_Paradas_EnvioActivo");
        b.HasIndex(p => new { p.RutaId, p.EnvioId }).IsUnique();
    }
}
internal sealed class ValidacionConfiguration : IEntityTypeConfiguration<ValidacionRuta>
{
    public void Configure(EntityTypeBuilder<ValidacionRuta> b)
    {
        b.ToTable("ValidacionesRuta"); b.Property(v => v.Operacion).HasConversion<string>();
        b.HasAlternateKey(v => new { v.OperadorId, v.Id });
        b.HasIndex(v => new { v.RutaId, v.RevisionRuta }).IsUnique();
        b.HasOne<Ruta>().WithMany().HasForeignKey(v => new { v.OperadorId, v.RutaId }).HasPrincipalKey(r => new { r.OperadorId, r.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(v => v.Bultos).WithOne().HasForeignKey(b => new { b.OperadorId, b.ValidacionRutaId }).HasPrincipalKey(v => new { v.OperadorId, v.Id });
    }
}
internal sealed class BultoValidacionConfiguration : IEntityTypeConfiguration<BultoValidacionRuta>
{
    public void Configure(EntityTypeBuilder<BultoValidacionRuta> b)
    { b.ToTable("BultosValidacionRuta"); b.HasIndex(d => new { d.ValidacionRutaId, d.BultoId }).IsUnique(); }
}

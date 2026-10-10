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

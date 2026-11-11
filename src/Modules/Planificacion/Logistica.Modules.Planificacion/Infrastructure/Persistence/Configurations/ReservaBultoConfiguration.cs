using Logistica.Modules.Planificacion.Domain.Rutas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence.Configurations;

internal sealed class ReservaBultoConfiguration : IEntityTypeConfiguration<ReservaBulto>
{
    public void Configure(EntityTypeBuilder<ReservaBulto> b)
    {
        b.ToTable("ReservasBulto");
        b.Property(r => r.Revision).IsConcurrencyToken();
        b.Property(r => r.CausaCierre).HasMaxLength(200);
        b.HasIndex(r => new { r.OperadorId, r.BultoId }).IsUnique()
            .HasFilter("\"CerradaEn\" IS NULL").HasDatabaseName("UX_ReservasBulto_Activa");
        b.HasIndex(r => new { r.OperadorId, r.RutaId, r.EnvioId });
    }
}

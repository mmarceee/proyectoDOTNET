using Logistica.Modules.Planificacion.Domain.Rutas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence.Configurations;

internal sealed class AnulacionDespachoConfiguration : IEntityTypeConfiguration<AnulacionDespacho>
{
    public void Configure(EntityTypeBuilder<AnulacionDespacho> b)
    {
        b.ToTable("AnulacionesDespacho");
        b.Property(a => a.Motivo).HasMaxLength(1000);
        b.HasIndex(a => new { a.RutaId, a.RevisionRuta }).IsUnique();
        b.HasOne<Ruta>().WithMany().HasForeignKey(a => new { a.OperadorId, a.RutaId })
            .HasPrincipalKey(r => new { r.OperadorId, r.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

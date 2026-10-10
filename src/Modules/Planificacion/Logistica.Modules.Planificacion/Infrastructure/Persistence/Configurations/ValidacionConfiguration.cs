using Logistica.Modules.Planificacion.Domain.Rutas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence.Configurations;

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

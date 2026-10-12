using Logistica.Modules.Planificacion.Domain.Rutas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence.Configurations;

internal sealed class RetiroEnvioRutaConfiguration : IEntityTypeConfiguration<RetiroEnvioRuta>
{
    public void Configure(EntityTypeBuilder<RetiroEnvioRuta> b)
    {
        b.ToTable("RetirosEnvioRuta");
        b.HasIndex(r => new { r.RutaId, r.RevisionRuta }).IsUnique();
        b.HasOne<Ruta>().WithMany().HasForeignKey(r => new { r.OperadorId, r.RutaId })
            .HasPrincipalKey(r => new { r.OperadorId, r.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

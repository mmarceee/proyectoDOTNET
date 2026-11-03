using Logistica.Modules.Administracion.Domain.Comercios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Configurations;

internal sealed class ClaveApiConfiguration : IEntityTypeConfiguration<ClaveApi>
{
    public void Configure(EntityTypeBuilder<ClaveApi> builder)
    {
        builder.ToTable("ClavesApi");
        builder.Property(c => c.Nombre).HasMaxLength(ClaveApi.LargoMaximoNombre);
        builder.Property(c => c.Tipo).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.Hash).HasMaxLength(64);
        builder.Property(c => c.Prefijo).HasMaxLength(ClaveApi.LargoPrefijo);
        builder.Property(c => c.RevocadaEn).IsConcurrencyToken();

        builder.HasIndex(c => c.Hash).IsUnique();
        builder.HasIndex(c => new { c.OperadorId, c.ComercioId, c.CreadaEn });
        builder.HasOne<RelacionComercial>().WithMany().HasForeignKey(c => c.RelacionComercialId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

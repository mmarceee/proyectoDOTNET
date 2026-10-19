using Logistica.Modules.Administracion.Domain.Tarifas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Configurations;

internal sealed class AjusteTarifaConfiguration : IEntityTypeConfiguration<AjusteTarifa>
{
    public void Configure(EntityTypeBuilder<AjusteTarifa> builder)
    {
        builder.ToTable("AjustesTarifa");
        builder.Property(a => a.Tipo).HasConversion<string>();
        builder.Property(a => a.Modalidad).HasConversion<string>();
    }
}

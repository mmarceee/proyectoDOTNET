using Logistica.Modules.Administracion.Domain.Tarifas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Configurations;

internal sealed class ReglaTarifaConfiguration : IEntityTypeConfiguration<ReglaTarifa>
{
    public void Configure(EntityTypeBuilder<ReglaTarifa> builder)
    {
        builder.ToTable("ReglasTarifa");
        builder.Property(r => r.Modalidad).HasConversion<string>();
    }
}

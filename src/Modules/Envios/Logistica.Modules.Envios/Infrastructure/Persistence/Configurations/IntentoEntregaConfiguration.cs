using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Configurations;

internal sealed class IntentoEntregaConfiguration : IEntityTypeConfiguration<IntentoEntrega>
{
    public void Configure(EntityTypeBuilder<IntentoEntrega> builder)
    {
        builder.ToTable("IntentosEntrega");
        builder.HasIndex(i => new { i.EnvioId, i.NumeroIntento }).IsUnique();
        builder.Property(i => i.Resultado).HasConversion<string>();
        builder.OwnsOne(i => i.Evidencia);
    }
}

using Logistica.Modules.Deposito.Domain.Liquidaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Deposito.Infrastructure.Persistence.Configurations;

internal sealed class LiquidacionConfiguration : IEntityTypeConfiguration<Liquidacion>
{
    public void Configure(EntityTypeBuilder<Liquidacion> builder)
    {
        builder.ToTable("Liquidaciones");

        builder.Property(l => l.Estado).HasConversion<string>();

        // Concurrencia optimista sobre xmin: dos administradores no emiten ni pagan la misma liquidación a la vez.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(l => l.Lineas).WithOne().HasForeignKey(l => l.LiquidacionId);

        builder.HasIndex(l => new { l.OperadorId, l.ComercioId, l.GeneradaEn });
    }
}

internal sealed class LineaLiquidacionConfiguration : IEntityTypeConfiguration<LineaLiquidacion>
{
    public const string IndiceEnvio = "UX_LineasLiquidacion_EnvioId";

    public void Configure(EntityTypeBuilder<LineaLiquidacion> builder)
    {
        builder.ToTable("LineasLiquidacion");

        // A2: un envío aparece en una sola liquidación. La base lo garantiza aunque se generen dos a la vez.
        builder.HasIndex(l => l.EnvioId).IsUnique().HasDatabaseName(IndiceEnvio);
    }
}

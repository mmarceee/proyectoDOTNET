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
    public const string IndiceOrigen = "UX_LineasLiquidacion_Origen";

    public void Configure(EntityTypeBuilder<LineaLiquidacion> builder)
    {
        builder.ToTable("LineasLiquidacion");

        builder.Property(l => l.Origen).HasConversion<string>().HasMaxLength(20);

        // A2 y D8: cada concepto (la base de un envío o un recargo) se cobra en una sola liquidación. La base lo garantiza
        // aunque se generen dos a la vez o los períodos se superpongan. Un envío puede tener su base y varios recargos.
        builder.HasIndex(l => new { l.OperadorId, l.Origen, l.IdDeOrigen }).IsUnique().HasDatabaseName(IndiceOrigen);
        builder.HasIndex(l => l.EnvioId);
    }
}

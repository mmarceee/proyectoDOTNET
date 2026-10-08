using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Domain.Incidencias;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Configurations;

internal sealed class IncidenciaConfiguration : IEntityTypeConfiguration<Incidencia>
{
    public void Configure(EntityTypeBuilder<Incidencia> builder)
    {
        builder.ToTable("Incidencias");
        builder.Property(i => i.Tipo).HasConversion<string>();
        builder.Property(i => i.Estado).HasConversion<string>();
        builder.HasIndex(i => new { i.OperadorId, i.ComercioId, i.EnvioId });
        builder.HasOne<Envio>().WithMany().HasForeignKey(i => i.EnvioId).OnDelete(DeleteBehavior.Restrict);
    }
}

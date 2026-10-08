using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Domain.Devoluciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Configurations;

internal sealed class DevolucionConfiguration : IEntityTypeConfiguration<Devolucion>
{
    public void Configure(EntityTypeBuilder<Devolucion> builder)
    {
        builder.ToTable("Devoluciones");
        builder.Property(d => d.Estado).HasConversion<string>();
        builder.HasIndex(d => d.EnvioId).IsUnique();
        builder.HasOne<Envio>().WithOne().HasForeignKey<Devolucion>(d => d.EnvioId).OnDelete(DeleteBehavior.Restrict);
    }
}

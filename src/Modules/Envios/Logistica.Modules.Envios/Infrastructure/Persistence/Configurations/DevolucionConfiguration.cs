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
        // RF 31: un envío puede tener varias devoluciones (un bulto suelto primero y el resto después), pero una sola
        // abierta a la vez.
        builder.HasIndex(d => d.EnvioId).IsUnique().HasFilter("\"Estado\" <> 'Cerrada'")
            .HasDatabaseName("UX_Devoluciones_EnvioAbierta");
        builder.HasOne<Envio>().WithMany().HasForeignKey(d => d.EnvioId).OnDelete(DeleteBehavior.Restrict);
    }
}

using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Configurations;

internal sealed class EventoEnvioConfiguration : IEntityTypeConfiguration<EventoEnvio>
{
    public void Configure(EntityTypeBuilder<EventoEnvio> builder)
    {
        builder.ToTable("EventosEnvio");

        builder.Property(e => e.EstadoAnterior).HasConversion<string>();
        builder.Property(e => e.EstadoNuevo).HasConversion<string>();
        builder.Property(e => e.Origen).HasConversion<string>();
    }
}

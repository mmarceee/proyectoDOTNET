using Logistica.Modules.Seguimiento.Domain.Avisos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence;

internal sealed class AvisoRutaConfiguration : IEntityTypeConfiguration<AvisoRuta>
{
    public void Configure(EntityTypeBuilder<AvisoRuta> b)
    {
        b.ToTable("avisos_ruta");
        b.HasKey(a => a.Id);
        b.HasIndex(a => new { a.OperadorId, a.RepartidorId, a.DespachadaEn });
        b.HasIndex(a => new { a.OperadorId, a.RutaId, a.Revision }).IsUnique();
    }
}

internal sealed class MensajeRecibidoConfiguration : IEntityTypeConfiguration<MensajeRecibido>
{
    public void Configure(EntityTypeBuilder<MensajeRecibido> b)
    {
        b.ToTable("inbox_messages");
        b.HasKey(m => new { m.MessageId, m.Consumidor });
        b.Property(m => m.Consumidor).HasMaxLength(200);
    }
}

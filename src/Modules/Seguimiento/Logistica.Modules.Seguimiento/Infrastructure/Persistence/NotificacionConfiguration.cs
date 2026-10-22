using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.Modules.Seguimiento.Domain.Notificaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence;

internal sealed class NotificacionConfiguration : IEntityTypeConfiguration<Notificacion>
{
    public void Configure(EntityTypeBuilder<Notificacion> b)
    {
        b.ToTable("notificaciones");
        b.HasKey(n => n.Id);
        // Una notificación por evento: el mismo evento procesado dos veces no manda dos correos (CU-62, requerimiento 1).
        b.HasIndex(n => n.EventoId).IsUnique();
        b.HasIndex(n => new { n.OperadorId, n.EnvioId });
        b.Property(n => n.Canal).HasConversion<string>().HasMaxLength(20);
        b.Property(n => n.Tipo).HasConversion<string>().HasMaxLength(30);
        b.Property(n => n.Estado).HasConversion<string>().HasMaxLength(20);
        b.Property(n => n.EstadoEnvio).HasMaxLength(30);
        b.Property(n => n.Destino).HasMaxLength(EnvioSeguido.LargoEmail);
        b.Property(n => n.UltimoError).HasMaxLength(Notificacion.LargoError);
        b.Ignore(n => n.PorEnviar);
    }
}

internal sealed class IdentidadOperadorConfiguration : IEntityTypeConfiguration<IdentidadOperador>
{
    public void Configure(EntityTypeBuilder<IdentidadOperador> b)
    {
        b.ToTable("identidades_operador");
        b.HasKey(i => i.Id);
        b.HasIndex(i => i.OperadorId).IsUnique();
        b.Property(i => i.Nombre).HasMaxLength(200);
        b.Property(i => i.ColorPrimario).HasMaxLength(20);
        b.Property(i => i.ColorSecundario).HasMaxLength(20);
        b.Property(i => i.EmailContacto).HasMaxLength(EnvioSeguido.LargoEmail);
        b.Property(i => i.TelefonoContacto).HasMaxLength(50);
    }
}

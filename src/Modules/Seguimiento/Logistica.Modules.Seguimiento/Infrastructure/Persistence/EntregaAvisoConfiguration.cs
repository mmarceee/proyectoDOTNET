using Logistica.Modules.Seguimiento.Domain.Avisos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence;

internal sealed class EntregaAvisoConfiguration : IEntityTypeConfiguration<EntregaAviso>
{
    public void Configure(EntityTypeBuilder<EntregaAviso> b)
    {
        b.ToTable("entregas_aviso");
        b.HasKey(e => e.Id);
        // Un hecho se avisa una sola vez a cada suscripción, aunque el evento llegue dos veces.
        b.HasIndex(e => new { e.EventoId, e.SuscripcionAvisoId }).IsUnique();
        // Historial del comercio (CU-65), filtrable por estado y fecha.
        b.HasIndex(e => new { e.OperadorId, e.ComercioId, e.Estado, e.FechaEvento });
        b.Property(e => e.NumeroEnvio).HasMaxLength(50);
        b.Property(e => e.EstadoEnvio).HasMaxLength(30);
        b.Property(e => e.Tipo).HasConversion<string>().HasMaxLength(30);
        b.Property(e => e.Estado).HasConversion<string>().HasMaxLength(30);
        b.HasMany(e => e.Intentos).WithOne().HasForeignKey(i => i.EntregaAvisoId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(e => e.Intentos).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class IntentoAvisoConfiguration : IEntityTypeConfiguration<IntentoAviso>
{
    public void Configure(EntityTypeBuilder<IntentoAviso> b)
    {
        b.ToTable("intentos_aviso");
        b.HasKey(i => i.Id);
        b.HasIndex(i => new { i.EntregaAvisoId, i.Numero }).IsUnique();
        b.Property(i => i.Error).HasMaxLength(IntentoAviso.LargoError);
    }
}

internal sealed class EnvioSeguidoConfiguration : IEntityTypeConfiguration<EnvioSeguido>
{
    public void Configure(EntityTypeBuilder<EnvioSeguido> b)
    {
        b.ToTable("envios_seguidos");
        b.HasKey(e => e.Id);
        b.Property(e => e.Numero).HasMaxLength(50);
    }
}

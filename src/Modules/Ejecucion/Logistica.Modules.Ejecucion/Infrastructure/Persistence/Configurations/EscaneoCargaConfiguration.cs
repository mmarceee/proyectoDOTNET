using Logistica.Modules.Ejecucion.Domain.Cargas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Ejecucion.Infrastructure.Persistence.Configurations;

internal sealed class EscaneoCargaConfiguration : IEntityTypeConfiguration<EscaneoCarga>
{
    public void Configure(EntityTypeBuilder<EscaneoCarga> b)
    {
        b.ToTable("escaneos_carga");
        b.HasKey(e => e.Id);
        b.Property(e => e.CodigoBulto).HasMaxLength(200);
        b.Property(e => e.Resultado).HasMaxLength(20);
        b.HasIndex(e => new { e.OperadorId, e.OperacionId, e.CodigoBulto }).IsUnique();
        b.HasIndex(e => new { e.OperadorId, e.RutaId });
    }
}

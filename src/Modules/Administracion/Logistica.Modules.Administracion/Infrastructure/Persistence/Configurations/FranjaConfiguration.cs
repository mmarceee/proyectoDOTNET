using Logistica.Modules.Administracion.Domain.Planificacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Configurations;

internal sealed class FranjaConfiguration : IEntityTypeConfiguration<FranjaHoraria>
{
    public void Configure(EntityTypeBuilder<FranjaHoraria> b)
    {
        b.ToTable("FranjasHorarias"); b.HasIndex(e => new { e.OperadorId, e.ZonaId, e.VigenteDesde });
        b.HasIndex(e => e.ReemplazaAId).IsUnique();
    }
}

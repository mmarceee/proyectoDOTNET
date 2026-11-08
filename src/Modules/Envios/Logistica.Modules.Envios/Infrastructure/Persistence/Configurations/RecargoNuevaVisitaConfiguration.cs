using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Configurations;

// RF 33: los recargos de las visitas adicionales, en su propia tabla, para que la liquidación los lea por período.
internal sealed class RecargoNuevaVisitaConfiguration : IEntityTypeConfiguration<RecargoNuevaVisita>
{
    public void Configure(EntityTypeBuilder<RecargoNuevaVisita> builder)
    {
        builder.ToTable("RecargosNuevaVisita");
        // La consulta de la liquidación (CU-32): un comercio del operador en un período.
        builder.HasIndex(r => new { r.OperadorId, r.ComercioId, r.OcurridoEn });
    }
}

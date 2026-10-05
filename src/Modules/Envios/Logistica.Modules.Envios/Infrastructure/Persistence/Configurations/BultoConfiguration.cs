using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Configurations;

internal sealed class BultoConfiguration : IEntityTypeConfiguration<Bulto>
{
    public void Configure(EntityTypeBuilder<Bulto> builder)
    {
        builder.ToTable("Bultos");

        // El código es lo que se escanea en el depósito: único dentro de cada operador (CU-30).
        builder.HasIndex(b => new { b.OperadorId, b.Codigo }).IsUnique();
    }
}

using Logistica.Modules.Administracion.Domain.Comercios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Configurations;

internal sealed class ComercioConfiguration : IEntityTypeConfiguration<Comercio>
{
    public void Configure(EntityTypeBuilder<Comercio> builder)
    {
        builder.ToTable("Comercios");

        // Único en toda la plataforma, no por operador: el comercio es global (modelo de dominio).
        builder.HasIndex(c => c.DocumentoFiscal).IsUnique();
    }
}

using Logistica.Modules.Administracion.Domain.Operadores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Configurations;

internal sealed class OperadorConfiguration : IEntityTypeConfiguration<Operador>
{
    public void Configure(EntityTypeBuilder<Operador> builder)
    {
        builder.ToTable("Operadores");

        // El slug identifica al operador en la plataforma: no puede repetirse.
        builder.HasIndex(o => o.Slug).IsUnique();
    }
}

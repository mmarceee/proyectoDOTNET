using Logistica.Modules.Administracion.Domain.Operadores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Configurations;

internal sealed class LogoOperadorConfiguration : IEntityTypeConfiguration<LogoOperador>
{
    public void Configure(EntityTypeBuilder<LogoOperador> builder)
    {
        builder.ToTable("Logos");
        builder.Property(l => l.TipoContenido).HasMaxLength(50);
        builder.Property(l => l.Contenido).HasMaxLength(LogoOperador.MaximoBytes);
    }
}

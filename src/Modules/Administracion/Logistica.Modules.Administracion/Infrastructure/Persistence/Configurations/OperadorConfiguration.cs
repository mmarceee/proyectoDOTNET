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

        // La identidad visual va en columnas de Operadores, sin tabla propia (modelo de dominio: sin id propio).
        // Los valores por defecto completan los operadores que ya existían al agregar las columnas.
        builder.ComplexProperty(o => o.Identidad, identidad =>
        {
            identidad.Property(i => i.ColorPrimario).HasMaxLength(7).HasDefaultValue("#0D6EFD");
            identidad.Property(i => i.ColorSecundario).HasMaxLength(7).HasDefaultValue("#6C757D");

            // Las propiedades de sólo lectura no se mapean solas: EF las necesita para usar el constructor del record.
            identidad.Property(i => i.LogoArchivoId);
            identidad.Property(i => i.EmailContacto).HasMaxLength(200);
            identidad.Property(i => i.TelefonoContacto).HasMaxLength(50);
        });
    }
}

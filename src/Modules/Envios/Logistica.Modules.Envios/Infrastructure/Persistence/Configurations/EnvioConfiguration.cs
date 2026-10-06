using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Configurations;

internal sealed class EnvioConfiguration : IEntityTypeConfiguration<Envio>
{
    public void Configure(EntityTypeBuilder<Envio> builder)
    {
        builder.ToTable("Envios");

        // El número es único dentro de cada operador (CU-10).
        builder.HasIndex(e => new { e.OperadorId, e.Numero }).IsUnique();

        builder.Property(e => e.Estado).HasConversion<string>();

        // Objetos valor: se guardan como columnas de la misma tabla (Destinatario_Nombre, Direccion_Calle...).
        builder.ComplexProperty(e => e.Destinatario);
        builder.ComplexProperty(e => e.Direccion);

        builder.HasMany(e => e.Bultos).WithOne().HasForeignKey(b => b.EnvioId);
        builder.HasMany(e => e.Eventos).WithOne().HasForeignKey(ev => ev.EnvioId);

        // Índices para el listado y los filtros de CU-13.
        builder.HasIndex(e => new { e.OperadorId, e.CreadoEn });
        builder.HasIndex(e => new { e.OperadorId, e.Estado });
    }
}

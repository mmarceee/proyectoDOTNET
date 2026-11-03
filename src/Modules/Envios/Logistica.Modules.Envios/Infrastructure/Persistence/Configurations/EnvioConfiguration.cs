using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Administracion.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Configurations;

internal sealed class EnvioConfiguration : IEntityTypeConfiguration<Envio>
{
    public const string IndiceReferencia = "UX_Envios_ReferenciaComercio";

    public void Configure(EntityTypeBuilder<Envio> builder)
    {
        builder.ToTable("Envios");

        // El número es único dentro de cada operador (CU-10).
        builder.HasIndex(e => new { e.OperadorId, e.Numero }).IsUnique();

        // CU-11 (RF 7): la referencia del comercio es única por operador y comercio, es decir, por relación comercial.
        // Es la garantía contra dos importaciones simultáneas (A4). Los envíos sin referencia (null) no chocan entre sí,
        // porque PostgreSQL considera distintos los NULL de un índice único.
        builder.Property(e => e.ReferenciaComercio).HasMaxLength(Envio.LargoMaximoReferencia);
        builder.HasIndex(e => new { e.OperadorId, e.ComercioId, e.ReferenciaComercio }).IsUnique()
            .HasDatabaseName(IndiceReferencia);

        builder.Property(e => e.Estado).HasConversion<string>();
        // RF 31: el resumen derivado de los bultos se guarda para filtrar sin reconstruirlo.
        builder.Property(e => e.Resumen).HasConversion<string>();
        builder.Property(e => e.Modalidad).HasConversion<string>().HasDefaultValue(Modalidad.Estandar);

        // Concurrencia optimista sobre xmin, la versión de fila de PostgreSQL: si dos operaciones
        // cambian el mismo envío a la vez (por ejemplo, recepción y cancelación), la segunda falla
        // en lugar de pisar a la primera (CU-16 y CU-30).
        builder.Property<uint>("Version").IsRowVersion();

        // Objetos valor: se guardan como columnas de la misma tabla (Destinatario_Nombre, Direccion_Calle...).
        builder.ComplexProperty(e => e.Destinatario);
        builder.ComplexProperty(e => e.Direccion);

        builder.HasMany(e => e.Bultos).WithOne().HasForeignKey(b => b.EnvioId);
        builder.HasMany(e => e.Eventos).WithOne().HasForeignKey(ev => ev.EnvioId);
        builder.HasMany(e => e.Intentos).WithOne().HasForeignKey(i => i.EnvioId);

        // Índices para el listado y los filtros de CU-13.
        builder.HasIndex(e => new { e.OperadorId, e.CreadoEn });
        builder.HasIndex(e => new { e.OperadorId, e.Estado });
        builder.HasIndex(e => new { e.OperadorId, e.Resumen });
    }
}

using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Administracion.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Configurations;

internal sealed class EnvioConfiguration : IEntityTypeConfiguration<Envio>
{
    public const string IndiceReferencia = "UX_Envios_ReferenciaComercio";
    public const string FiltroPrueba = "Prueba";

    public void Configure(EntityTypeBuilder<Envio> builder)
    {
        builder.ToTable("Envios");

        // El número es único dentro de cada operador (CU-10).
        builder.HasIndex(e => new { e.OperadorId, e.Numero }).IsUnique();

        // CU-11 y CU-81: la referencia es única por relación comercial y ambiente.
        // Es la garantía contra dos importaciones simultáneas (A4). Los envíos sin referencia (null) no chocan entre sí,
        // porque PostgreSQL considera distintos los NULL de un índice único.
        builder.Property(e => e.ReferenciaComercio).HasMaxLength(Envio.LargoMaximoReferencia);
        builder.HasIndex(e => new { e.OperadorId, e.ComercioId, e.EsPrueba, e.ReferenciaComercio }).IsUnique()
            .HasDatabaseName(IndiceReferencia);
        builder.HasQueryFilter(FiltroPrueba, e => !e.EsPrueba);
        builder.Property(e => e.EsPrueba).HasDefaultValue(false);

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
        // Los bultos se cargan siempre con su envío (RF 31): el envío mueve a los bultos que lo acompañan y recalcula su
        // resumen a partir de ellos. Un envío cargado sin bultos los dejaría atrás (un extravío, una cancelación) y
        // calcularía el resumen sobre una lista vacía, con Terminado en true.
        builder.Navigation(e => e.Bultos).AutoInclude();
        builder.HasMany(e => e.Eventos).WithOne().HasForeignKey(ev => ev.EnvioId);
        builder.HasMany(e => e.Intentos).WithOne().HasForeignKey(i => i.EnvioId);
        builder.HasMany(e => e.Recargos).WithOne().HasForeignKey(r => r.EnvioId);
        // Se calcula con los bultos en memoria (los que están en el estado del envío); no es una relación propia.
        builder.Ignore(e => e.BultosQueAcompanan);
        builder.Ignore(e => e.BultosParaCarga);
        builder.Ignore(e => e.PendientesDeEntrega);

        // Índices para el listado y los filtros de CU-13.
        builder.HasIndex(e => new { e.OperadorId, e.CreadoEn });
        builder.HasIndex(e => new { e.OperadorId, e.Estado });
        builder.HasIndex(e => new { e.OperadorId, e.Resumen });
    }
}

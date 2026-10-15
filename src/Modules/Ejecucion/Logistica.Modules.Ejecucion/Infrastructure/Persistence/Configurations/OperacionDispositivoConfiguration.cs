using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Ejecucion.Infrastructure.Persistence.Configurations;

internal sealed class OperacionDispositivoConfiguration : IEntityTypeConfiguration<OperacionDispositivo>
{
    public void Configure(EntityTypeBuilder<OperacionDispositivo> builder)
    {
        // El nombre del ADR-0004 (sección 2.10). La clave es el OperacionId del dispositivo: la base impide aplicar
        // dos veces la misma operación aunque lleguen dos reenvíos a la vez.
        builder.ToTable("operaciones_dispositivo");

        builder.Property(o => o.Tipo).HasMaxLength(40);
        builder.Property(o => o.Resultado).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Motivo).HasMaxLength(60);
        builder.Property(o => o.Detalle).HasMaxLength(500);

        // Los conflictos pendientes de revisión, para el tablero del despachador (CU-66).
        builder.HasIndex(o => new { o.OperadorId, o.Resultado, o.Revisada });
    }
}

using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.BuildingBlocks.Infrastructure.Messaging.Fallidos;

// DbContext técnico de la mensajería, en el schema "mensajeria" (ADR-0003, sección 2.6). Hereda de ModuleDbContext: el
// filtro y el interceptor de inquilino se aplican igual que en los módulos.
public sealed class MensajeriaDbContext(DbContextOptions<MensajeriaDbContext> options, UnidadDeTrabajo unidad, ICurrentTenant tenant)
    : ModuleDbContext(options, Schema, unidad, tenant)
{
    public const string Schema = "mensajeria";
    public DbSet<MensajeFallido> MensajesFallidos => Set<MensajeFallido>();
}

internal sealed class MensajeFallidoConfiguration : IEntityTypeConfiguration<MensajeFallido>
{
    public void Configure(EntityTypeBuilder<MensajeFallido> b)
    {
        b.ToTable("mensajes_fallidos");
        b.HasKey(m => m.Id);
        // Si el registrador recibe dos veces el mismo mensaje de la misma cola, actualiza la fila (ADR-0003, sección 2.6).
        b.HasIndex(m => new { m.MessageId, m.ColaOrigen }).IsUnique();
        b.HasIndex(m => new { m.OperadorId, m.Estado, m.ActualizadoEn });
        b.Property(m => m.ColaOrigen).HasMaxLength(200);
        b.Property(m => m.Tipo).HasMaxLength(200);
        b.Property(m => m.TraceParent).HasMaxLength(55);
        b.Property(m => m.UltimoError).HasMaxLength(MensajeFallido.LargoError);
        b.Property(m => m.Estado).HasConversion<string>().HasMaxLength(20);
        b.Property(m => m.ResueltoPor).HasMaxLength(256);
        b.Property(m => m.MotivoDescarte).HasMaxLength(MensajeFallido.LargoMotivo);
        b.PrimitiveCollection(m => m.Historial).HasField("_historial").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

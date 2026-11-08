using Logistica.Modules.Seguimiento.Domain.Indicadores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence;

internal sealed class RegistroCumplimientoConfiguration : IEntityTypeConfiguration<RegistroCumplimiento>
{
    public void Configure(EntityTypeBuilder<RegistroCumplimiento> b)
    {
        b.ToTable("registros_cumplimiento");
        b.HasKey(r => r.Id);
        // Los reportes (CU-72) filtran por operador, tipo y período, y agrupan por zona, repartidor, comercio o motivo.
        b.HasIndex(r => new { r.OperadorId, r.Tipo, r.OcurridoEn });
        b.Property(r => r.Tipo).HasConversion<string>().HasMaxLength(30);
        b.Property(r => r.Motivo).HasMaxLength(RegistroCumplimiento.LargoMotivo);
    }
}

internal sealed class RegistrosCumplimientoRepository(SeguimientoDbContext db) : IRegistrosCumplimientoRepository
{
    public void Agregar(RegistroCumplimiento registro) => db.RegistrosCumplimiento.Add(registro);

    public async Task GuardarAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
}

using Logistica.Modules.Administracion.Domain.CierresDiarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

internal sealed class CierresDiariosRepository(AdministracionDbContext db) : ICierresDiariosRepository
{
    public Task<bool> ExisteAsync(DateOnly fecha, CancellationToken ct) =>
        db.CierresDiarios.AnyAsync(c => c.Fecha == fecha, ct);

    public void Agregar(CierreDiario cierre) => db.CierresDiarios.Add(cierre);

    public async Task GuardarAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
}

internal sealed class CierreDiarioConfiguration : IEntityTypeConfiguration<CierreDiario>
{
    public void Configure(EntityTypeBuilder<CierreDiario> b)
    {
        b.ToTable("cierres_diarios");
        b.HasKey(c => c.Id);
        // A1: un cierre por operador y día, aunque dos vueltas lo intenten a la vez.
        b.HasIndex(c => new { c.OperadorId, c.Fecha }).IsUnique();
    }
}

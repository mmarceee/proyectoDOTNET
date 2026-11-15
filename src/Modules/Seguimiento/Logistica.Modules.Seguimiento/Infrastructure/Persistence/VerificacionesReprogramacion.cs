using Logistica.Modules.Seguimiento.Domain.Reprogramaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence;

internal sealed class VerificacionReprogramacionConfiguration : IEntityTypeConfiguration<VerificacionReprogramacion>
{
    public void Configure(EntityTypeBuilder<VerificacionReprogramacion> b)
    {
        b.ToTable("verificaciones_reprogramacion");
        b.HasKey(v => v.Id);
        // La última por envío y los pedidos de la última hora se buscan por envío y fecha.
        b.HasIndex(v => new { v.OperadorId, v.EnvioId, v.CreadaEn });
        b.Property(v => v.Medio).HasConversion<string>().HasMaxLength(20);
        b.Property(v => v.HashSecreto).HasMaxLength(32);
        b.Ignore(v => v.IntentosRestantes);
    }
}

internal sealed class VerificacionesReprogramacionRepository(SeguimientoDbContext db) : IVerificacionesReprogramacionRepository
{
    public void Agregar(VerificacionReprogramacion verificacion) => db.VerificacionesReprogramacion.Add(verificacion);

    public async Task<IReadOnlyList<VerificacionReprogramacion>> ListarAbiertasAsync(Guid envioId, CancellationToken ct) =>
        await db.VerificacionesReprogramacion
            .Where(v => v.EnvioId == envioId && v.UsadaEn == null && !v.Anulada)
            .ToListAsync(ct);

    public Task<int> ContarDesdeAsync(Guid envioId, DateTimeOffset desde, CancellationToken ct) =>
        db.VerificacionesReprogramacion.CountAsync(v => v.EnvioId == envioId && v.CreadaEn >= desde, ct);

    public Task<VerificacionReprogramacion?> UltimaAsync(Guid envioId, CancellationToken ct) =>
        db.VerificacionesReprogramacion
            .Where(v => v.EnvioId == envioId)
            .OrderByDescending(v => v.CreadaEn)
            .FirstOrDefaultAsync(ct);

    public async Task GuardarAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
}

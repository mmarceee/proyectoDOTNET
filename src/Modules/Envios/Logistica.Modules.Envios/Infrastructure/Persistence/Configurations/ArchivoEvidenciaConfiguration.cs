using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Envios.Infrastructure.Persistence.Configurations;

internal sealed class ArchivoEvidenciaConfiguration : IEntityTypeConfiguration<ArchivoEvidencia>
{
    public void Configure(EntityTypeBuilder<ArchivoEvidencia> builder)
    {
        builder.ToTable("ArchivosEvidencia");
        builder.HasIndex(a => new { a.OperadorId, a.ComercioId, a.EnvioId });
        builder.HasOne<Envio>().WithMany().HasForeignKey(a => a.EnvioId).OnDelete(DeleteBehavior.Restrict);
    }
}

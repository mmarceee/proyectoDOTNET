using Logistica.Modules.Deposito.Domain.Recepciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Deposito.Infrastructure.Persistence.Configurations;

internal sealed class OperacionCustodiaConfiguration : IEntityTypeConfiguration<OperacionCustodia>
{
    public void Configure(EntityTypeBuilder<OperacionCustodia> b)
    {
        b.ToTable("OperacionesCustodia");
        b.HasIndex(o => new { o.OperadorId, o.OperacionId }).IsUnique();
        b.Property(o => o.Huella).HasMaxLength(64);
    }
}

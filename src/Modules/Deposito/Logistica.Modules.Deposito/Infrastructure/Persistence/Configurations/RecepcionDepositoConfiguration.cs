using Logistica.Modules.Deposito.Domain.Recepciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Deposito.Infrastructure.Persistence.Configurations;

internal sealed class RecepcionDepositoConfiguration : IEntityTypeConfiguration<RecepcionDeposito>
{
    public void Configure(EntityTypeBuilder<RecepcionDeposito> builder)
    {
        builder.ToTable("Recepciones");

        // Un bulto se recibe una sola vez (CU-30, A2). La base lo garantiza aunque dos operarios
        // escaneen el mismo bulto a la vez.
        builder.HasIndex(r => r.BultoId).IsUnique();

        // Para saber qué bultos de un envío ya llegaron.
        builder.HasIndex(r => new { r.OperadorId, r.EnvioId });

        builder.Property(r => r.Resultado).HasConversion<string>();
    }
}

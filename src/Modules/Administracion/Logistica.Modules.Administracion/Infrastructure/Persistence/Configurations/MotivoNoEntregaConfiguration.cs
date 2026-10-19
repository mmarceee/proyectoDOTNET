using Logistica.Modules.Administracion.Domain.Reglas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Configurations;

internal sealed class MotivoNoEntregaConfiguration : IEntityTypeConfiguration<MotivoNoEntrega>
{
    public void Configure(EntityTypeBuilder<MotivoNoEntrega> builder)
    {
        builder.ToTable("MotivosNoEntrega");
    }
}

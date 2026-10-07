using Logistica.Modules.Administracion.Domain.Comercios;
using Logistica.Modules.Administracion.Domain.Operadores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Configurations;

internal sealed class RelacionComercialConfiguration : IEntityTypeConfiguration<RelacionComercial>
{
    public void Configure(EntityTypeBuilder<RelacionComercial> builder)
    {
        builder.ToTable("RelacionesComerciales");

        // Un comercio tiene a lo sumo una relación con cada operador.
        builder.HasIndex(r => new { r.OperadorId, r.ComercioId }).IsUnique();

        builder.Property(r => r.Estado).HasConversion<string>();

        // Las tres tablas son del mismo módulo, así que la base puede garantizar las claves foráneas.
        // Restrict: borrar un operador o un comercio no borra en cascada sus relaciones.
        builder.HasOne<Operador>().WithMany().HasForeignKey(r => r.OperadorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Comercio>().WithMany().HasForeignKey(r => r.ComercioId).OnDelete(DeleteBehavior.Restrict);
    }
}

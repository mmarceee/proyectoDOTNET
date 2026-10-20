using Logistica.Modules.Seguimiento.Domain.Avisos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence;

internal sealed class SuscripcionAvisoConfiguration : IEntityTypeConfiguration<SuscripcionAviso>
{
    public void Configure(EntityTypeBuilder<SuscripcionAviso> b)
    {
        b.ToTable("suscripciones_aviso");
        b.HasKey(s => s.Id);
        b.Property(s => s.UrlDestino).IsRequired();
        // Dos regeneraciones concurrentes no deben devolver como vigente un secreto que la otra pisa.
        b.Property(s => s.SecretoFirma).IsRequired().IsConcurrencyToken();
        b.PrimitiveCollection(s => s.TiposEvento)
            .HasField("_tiposEvento").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(s => new { s.OperadorId, s.ComercioId, s.Activa });
        // La relación pertenece a Administración: se referencia por Id, sin navegación entre módulos.
        b.HasIndex(s => s.RelacionComercialId);
    }
}

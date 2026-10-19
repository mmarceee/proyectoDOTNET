using System.Text.Json;
using Logistica.Modules.Administracion.Domain.Reglas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence.Configurations;

internal sealed class VersionReglasConfiguration : IEntityTypeConfiguration<VersionReglas>
{
    public void Configure(EntityTypeBuilder<VersionReglas> builder)
    {
        builder.ToTable("VersionesReglas");
        builder.Property(v => v.Estado).HasConversion<string>();

        // A3 (conflicto de concurrencia): igual que el tarifario, sobre xmin.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasIndex(v => new { v.OperadorId, v.Numero }).IsUnique();

        // Un borrador por operador: índice único parcial, como en el tarifario.
        builder.HasIndex(v => v.OperadorId).IsUnique()
            .HasFilter("\"Estado\" = 'Borrador'")
            .HasDatabaseName("UX_VersionesReglas_Borrador");

        // Pruebas exigidas y política de devolución: columnas de la misma tabla, sin tabla propia.
        builder.ComplexProperty(v => v.PruebaExigida);
        builder.ComplexProperty(v => v.PoliticaDevolucion);

        // Los plazos por modalidad (dos filas a lo sumo) se guardan como JSON en una columna. La lista se reemplaza entera
        // al modificar, así que comparar por contenido alcanza para que EF detecte el cambio.
        builder.Ignore(v => v.Plazos);
        builder.Property<List<PlazoModalidad>>("_plazos")
            .HasColumnName("Plazos")
            .HasColumnType("jsonb")
            .HasConversion(
                plazos => JsonSerializer.Serialize(plazos, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<PlazoModalidad>>(json, (JsonSerializerOptions?)null) ?? new List<PlazoModalidad>(),
                new ValueComparer<List<PlazoModalidad>>(
                    (a, b) => a!.SequenceEqual(b!),
                    plazos => plazos.Aggregate(0, (hash, plazo) => HashCode.Combine(hash, plazo.GetHashCode())),
                    plazos => plazos.ToList()));

        builder.HasMany(v => v.Motivos).WithOne().HasForeignKey(m => m.VersionReglasId);
    }
}

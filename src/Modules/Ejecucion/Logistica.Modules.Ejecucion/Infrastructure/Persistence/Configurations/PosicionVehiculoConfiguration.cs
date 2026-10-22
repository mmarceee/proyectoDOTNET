using Logistica.Modules.Ejecucion.Domain.Posiciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Logistica.Modules.Ejecucion.Infrastructure.Persistence.Configurations;

internal sealed class PosicionVehiculoConfiguration : IEntityTypeConfiguration<PosicionVehiculo>
{
    public void Configure(EntityTypeBuilder<PosicionVehiculo> b)
    {
        b.ToTable("posiciones_vehiculo", t =>
        {
            t.HasCheckConstraint("ck_posicion_latitud", "\"Latitud\" BETWEEN -90 AND 90");
            t.HasCheckConstraint("ck_posicion_longitud", "\"Longitud\" BETWEEN -180 AND 180");
        });
        b.HasKey(p => p.Id);
        b.Property(p => p.Latitud).HasPrecision(10, 6);
        b.Property(p => p.Longitud).HasPrecision(10, 6);
        b.HasIndex(p => new { p.OperadorId, p.VehiculoId, p.CapturadaEn, p.Id });
        b.HasIndex(p => p.CapturadaEn);
    }
}

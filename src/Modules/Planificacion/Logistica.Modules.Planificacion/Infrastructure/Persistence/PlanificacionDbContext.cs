using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence;

internal sealed class PlanificacionDbContext(DbContextOptions<PlanificacionDbContext> options, UnidadDeTrabajo unidad)
    : ModuleDbContext(options, Schema, unidad)
{
    public const string Schema = "planificacion";
    public DbSet<Ruta> Rutas => Set<Ruta>();
    public DbSet<Parada> Paradas => Set<Parada>();
    public DbSet<ValidacionRuta> Validaciones => Set<ValidacionRuta>();
}

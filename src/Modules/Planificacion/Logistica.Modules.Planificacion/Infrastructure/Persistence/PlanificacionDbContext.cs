using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence;

internal sealed class PlanificacionDbContext(DbContextOptions<PlanificacionDbContext> options, UnidadDeTrabajo unidad, ICurrentTenant tenant)
    : ModuleDbContext(options, Schema, unidad, tenant)
{
    public const string Schema = "planificacion";
    public DbSet<Ruta> Rutas => Set<Ruta>();
    public DbSet<Parada> Paradas => Set<Parada>();
    public DbSet<ValidacionRuta> Validaciones => Set<ValidacionRuta>();
}

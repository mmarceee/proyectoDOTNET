using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Ejecucion.Infrastructure.Persistence;

internal sealed class EjecucionDbContext(DbContextOptions<EjecucionDbContext> options, UnidadDeTrabajo unidadDeTrabajo, ICurrentTenant tenant)
    : ModuleDbContext(options, Schema, unidadDeTrabajo, tenant)
{
    public const string Schema = "ejecucion";
    public DbSet<Domain.Posiciones.PosicionVehiculo> Posiciones => Set<Domain.Posiciones.PosicionVehiculo>();

    public DbSet<OperacionDispositivo> Operaciones => Set<OperacionDispositivo>();
    public DbSet<Domain.Cargas.EscaneoCarga> EscaneosCarga => Set<Domain.Cargas.EscaneoCarga>();
    public DbSet<Domain.Rendiciones.Rendicion> Rendiciones => Set<Domain.Rendiciones.Rendicion>();
}

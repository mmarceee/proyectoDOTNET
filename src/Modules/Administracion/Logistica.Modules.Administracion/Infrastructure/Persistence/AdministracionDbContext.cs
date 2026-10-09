using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Administracion.Domain.Comercios;
using Logistica.Modules.Administracion.Domain.Operadores;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

internal sealed class AdministracionDbContext(DbContextOptions<AdministracionDbContext> options, UnidadDeTrabajo unidadDeTrabajo, ICurrentTenant tenant)
    : ModuleDbContext(options, Schema, unidadDeTrabajo, tenant)
{
    public const string Schema = "administracion";

    public DbSet<Operador> Operadores => Set<Operador>();
    public DbSet<Comercio> Comercios => Set<Comercio>();
    public DbSet<RelacionComercial> RelacionesComerciales => Set<RelacionComercial>();
}

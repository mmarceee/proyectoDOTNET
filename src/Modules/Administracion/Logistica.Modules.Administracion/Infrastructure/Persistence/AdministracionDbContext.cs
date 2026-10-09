using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Administracion.Domain.Comercios;
using Logistica.Modules.Administracion.Domain.Operadores;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

internal sealed class AdministracionDbContext(DbContextOptions<AdministracionDbContext> options, UnidadDeTrabajo unidadDeTrabajo)
    : ModuleDbContext(options, Schema, unidadDeTrabajo)
{
    public const string Schema = "administracion";

    public DbSet<Operador> Operadores => Set<Operador>();
    public DbSet<Comercio> Comercios => Set<Comercio>();
    public DbSet<RelacionComercial> RelacionesComerciales => Set<RelacionComercial>();
    public DbSet<Domain.Planificacion.Repartidor> Repartidores => Set<Domain.Planificacion.Repartidor>();
    public DbSet<Domain.Planificacion.Vehiculo> Vehiculos => Set<Domain.Planificacion.Vehiculo>();
    public DbSet<Domain.Planificacion.Zona> Zonas => Set<Domain.Planificacion.Zona>();
    public DbSet<Domain.Planificacion.FranjaHoraria> Franjas => Set<Domain.Planificacion.FranjaHoraria>();
    public DbSet<Domain.Planificacion.VersionReglasPlanificacion> ReglasPlanificacion => Set<Domain.Planificacion.VersionReglasPlanificacion>();
}

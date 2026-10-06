using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Deposito.Domain.Recepciones;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Deposito.Infrastructure.Persistence;

internal sealed class DepositoDbContext(DbContextOptions<DepositoDbContext> options, UnidadDeTrabajo unidadDeTrabajo)
    : ModuleDbContext(options, Schema, unidadDeTrabajo)
{
    public const string Schema = "deposito";

    public DbSet<RecepcionDeposito> Recepciones => Set<RecepcionDeposito>();
}

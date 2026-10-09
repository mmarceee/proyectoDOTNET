using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Deposito.Domain.Recepciones;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Deposito.Infrastructure.Persistence;

internal sealed class DepositoDbContext(DbContextOptions<DepositoDbContext> options, UnidadDeTrabajo unidadDeTrabajo, ICurrentTenant tenant)
    : ModuleDbContext(options, Schema, unidadDeTrabajo, tenant)
{
    public const string Schema = "deposito";

    public DbSet<RecepcionDeposito> Recepciones => Set<RecepcionDeposito>();
}

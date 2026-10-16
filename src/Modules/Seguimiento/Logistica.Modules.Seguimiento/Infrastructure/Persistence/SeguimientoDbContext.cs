using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence;

internal sealed class SeguimientoDbContext(DbContextOptions<SeguimientoDbContext> options, UnidadDeTrabajo unidad, ICurrentTenant tenant)
    : ModuleDbContext(options, Schema, unidad, tenant)
{
    public const string Schema = "seguimiento";
    public DbSet<AvisoRuta> Avisos => Set<AvisoRuta>();
    public DbSet<MensajeRecibido> Inbox => Set<MensajeRecibido>();
}

internal sealed class MensajeRecibido
{
    public Guid MessageId { get; set; }
    public string Consumidor { get; set; } = "";
    public DateTimeOffset ProcesadoEn { get; set; }
}

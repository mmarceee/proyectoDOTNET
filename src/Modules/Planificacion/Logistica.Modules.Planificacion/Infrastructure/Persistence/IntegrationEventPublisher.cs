using Logistica.Modules.Planificacion.Application.Abstractions;

namespace Logistica.Modules.Planificacion.Infrastructure.Persistence;

internal sealed class IntegrationEventPublisher(PlanificacionDbContext db) : IIntegrationEventPublisher
{
    public void Registrar<T>(string tipo, T evento, DateTimeOffset ocurridoEn)
        => db.AgregarAlOutbox(tipo, evento, ocurridoEn);
}

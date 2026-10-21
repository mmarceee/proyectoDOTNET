using Logistica.Modules.Seguimiento.Application;

namespace Logistica.Modules.Seguimiento.Infrastructure.Persistence;

internal sealed class IntegrationEventPublisher(SeguimientoDbContext db) : IIntegrationEventPublisher
{
    public void Registrar<T>(string tipo, T evento, DateTimeOffset ocurridoEn)
    {
        db.AgregarAlOutbox(tipo, evento, ocurridoEn);
    }
}

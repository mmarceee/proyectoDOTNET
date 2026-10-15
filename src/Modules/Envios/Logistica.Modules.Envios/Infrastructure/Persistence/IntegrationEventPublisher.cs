using Logistica.Modules.Envios.Application;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

internal sealed class IntegrationEventPublisher(EnviosDbContext db) : IIntegrationEventPublisher
{
    public void Registrar<T>(string tipo, T evento, DateTimeOffset ocurridoEn)
    {
        db.AgregarAlOutbox(tipo, evento, ocurridoEn);
    }
}

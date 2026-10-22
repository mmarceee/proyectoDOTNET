using Logistica.Modules.Administracion.Application;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

internal sealed class IntegrationEventPublisher(AdministracionDbContext db) : IIntegrationEventPublisher
{
    public void Registrar<T>(string tipo, T evento, DateTimeOffset ocurridoEn)
    {
        db.AgregarAlOutbox(tipo, evento, ocurridoEn);
    }
}

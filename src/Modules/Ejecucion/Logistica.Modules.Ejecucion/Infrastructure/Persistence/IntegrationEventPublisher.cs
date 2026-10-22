using Logistica.Modules.Ejecucion.Application.Abstractions;

namespace Logistica.Modules.Ejecucion.Infrastructure.Persistence;

internal sealed class IntegrationEventPublisher(EjecucionDbContext db) : IIntegrationEventPublisher
{
    public void Registrar<T>(string tipo, T evento, DateTimeOffset ocurridoEn) => db.AgregarAlOutbox(tipo, evento, ocurridoEn);
}

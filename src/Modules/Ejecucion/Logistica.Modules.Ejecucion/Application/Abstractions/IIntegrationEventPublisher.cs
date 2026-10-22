namespace Logistica.Modules.Ejecucion.Application.Abstractions;

// ADR-0003 §2.3: Application decide qué registrar; Infrastructure implementa el Outbox.
internal interface IIntegrationEventPublisher
{
    void Registrar<T>(string tipo, T evento, DateTimeOffset ocurridoEn);
}

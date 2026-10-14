namespace Logistica.Modules.Planificacion.Application.Abstractions;

internal interface IIntegrationEventPublisher
{
    void Registrar<T>(string tipo, T evento, DateTimeOffset ocurridoEn);
}

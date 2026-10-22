namespace Logistica.Modules.Seguimiento.Application;

// Registra un evento de integración en el outbox de Seguimiento (ADR-0003, sección 2.3). Se guarda con el próximo
// guardado del módulo, junto con el cambio que lo origina.
internal interface IIntegrationEventPublisher
{
    void Registrar<T>(string tipo, T evento, DateTimeOffset ocurridoEn);
}

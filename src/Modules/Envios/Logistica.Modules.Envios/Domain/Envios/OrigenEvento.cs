namespace Logistica.Modules.Envios.Domain.Envios;

// Desde dónde se produjo un cambio de estado (RF 12).
internal enum OrigenEvento
{
    PortalComercio,
    Backoffice,
    AppRepartidor,
    Sistema,
    SeguimientoPublico,
    Api,
}

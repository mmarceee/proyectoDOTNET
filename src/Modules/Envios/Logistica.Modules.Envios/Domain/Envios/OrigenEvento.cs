namespace Logistica.Modules.Envios.Domain.Envios;

// Desde dónde se produjo un cambio de estado (RF 12).
internal enum OrigenEvento
{
    PortalComercio,
    BackOffice,
    AppRepartidor,
    Sistema,
    SeguimientoPublico,
    Api,
}

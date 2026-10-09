namespace Logistica.Modules.Envios.Presentation;

internal static class OrigenEventoTexto
{
    public static string Mostrar(string origen) => origen switch
    {
        "PortalComercio" => "Portal del comercio",
        "Backoffice" => "Backoffice",
        "AppRepartidor" => "App del repartidor",
        "Sistema" => "Sistema",
        "SeguimientoPublico" => "Seguimiento público",
        "Api" => "API",
        _ => origen,
    };
}

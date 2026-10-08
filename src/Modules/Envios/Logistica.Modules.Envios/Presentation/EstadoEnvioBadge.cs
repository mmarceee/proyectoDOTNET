namespace Logistica.Modules.Envios.Presentation;

internal static class EstadoEnvioBadge
{
    public static string Clase(string estado) => estado switch
    {
        "Entregado" => "text-bg-success",
        "Devuelto" or "EnDevolucion" => "badge-envio-devolucion",
        "NoEntregado" or "Extraviado" or "Cancelado" => "text-bg-danger",
        "Reprogramado" => "text-bg-warning",
        "AsignadoARuta" or "EnTransito" => "text-bg-primary",
        _ => "text-bg-secondary",
    };
}

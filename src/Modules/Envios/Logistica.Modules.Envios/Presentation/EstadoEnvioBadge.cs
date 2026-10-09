namespace Logistica.Modules.Envios.Presentation;

internal static class EstadoEnvioBadge
{
    public static string Clase(string estado) => estado switch
    {
        "Admitido" => "text-bg-info",
        "EnDeposito" => "text-bg-secondary",
        "AsignadoARuta" => "text-bg-primary",
        "EnTransito" => "badge-envio-transito",
        "Entregado" => "text-bg-success",
        "NoEntregado" => "text-bg-danger",
        "Reprogramado" => "text-bg-warning",
        "EnDevolucion" => "badge-envio-en-devolucion",
        "Devuelto" => "badge-envio-devolucion",
        "Extraviado" => "text-bg-dark",
        "Cancelado" => "badge-envio-cancelado",
        _ => "text-bg-secondary",
    };
}

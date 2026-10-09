namespace Logistica.Modules.Envios.Presentation;

internal static class EstadoEnvioTexto
{
    public static string Mostrar(string estado) => estado switch
    {
        "EnDeposito" => "En depósito",
        "AsignadoARuta" => "Asignado a ruta",
        "EnTransito" => "En tránsito",
        "NoEntregado" => "No entregado",
        "EnDevolucion" => "En devolución",
        _ => estado,
    };
}

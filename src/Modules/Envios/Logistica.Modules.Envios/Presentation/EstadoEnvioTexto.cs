namespace Logistica.Modules.Envios.Presentation;

internal static class EstadoEnvioTexto
{
    public static string Resumen(string resumen, bool terminado = false, int entregados = 0) =>
        terminado && entregados == 0 ? "Finalizado sin entrega" : resumen switch
        {
            "EnOperacion" => "En operación",
            "EntregadoParcialmente" => "Entregado parcialmente",
            _ => resumen,
        };

    public static string Modalidad(string modalidad) => modalidad == "Estandar" ? "Estándar" : modalidad;

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

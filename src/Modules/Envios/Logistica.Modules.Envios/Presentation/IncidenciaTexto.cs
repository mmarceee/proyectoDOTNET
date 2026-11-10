namespace Logistica.Modules.Envios.Presentation;

// CU-22: los nombres de los tipos y estados de una incidencia como los lee el usuario, y el color de cada estado.
internal static class IncidenciaTexto
{
    public static string Tipo(string tipo) => tipo switch
    {
        "Extravio" => "Extravío",
        "DañoEnBulto" => "Daño en bulto",
        _ => tipo,
    };

    public static string Estado(string estado) => estado == "EnProceso" ? "En proceso" : estado;

    public static string ClaseEstado(string estado) => estado switch
    {
        "Abierta" => "text-bg-danger",
        "EnProceso" => "text-bg-warning",
        _ => "text-bg-success",
    };
}

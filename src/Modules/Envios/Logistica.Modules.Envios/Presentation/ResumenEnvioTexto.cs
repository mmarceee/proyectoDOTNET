namespace Logistica.Modules.Envios.Presentation;

// RF 31 y RF 35: cómo se muestra el resumen del envío (decisiones-entrega-parcial.md, secciones 2 y 3).
// «Finalizado sin entrega» es sólo presentación: un envío terminado sin ningún bulto entregado.
internal static class ResumenEnvioTexto
{
    public static string Mostrar(string resumen, bool terminado, int entregados) =>
        SinEntrega(terminado, entregados) ? "Finalizado sin entrega" : resumen switch
        {
            "EnOperacion" => "En operación",
            "EntregadoParcialmente" => "Entregado parcialmente",
            _ => resumen,
        };

    public static string Clase(string resumen, bool terminado, int entregados) =>
        SinEntrega(terminado, entregados) ? "badge-envio-cancelado" : resumen switch
        {
            "Creado" => "text-bg-info",
            "EnOperacion" => "text-bg-primary",
            "EntregadoParcialmente" => "text-bg-warning",
            "Entregado" => "text-bg-success",
            _ => "text-bg-secondary",
        };

    // El contador «2 de 3 entregados»; en una entrega parcial agrega si sigue en curso o ya terminó.
    public static string Detalle(string resumen, bool terminado, int entregados, int total)
    {
        var contador = $"{entregados} de {total} entregados";
        return resumen == "EntregadoParcialmente" ? $"{contador} · {(terminado ? "Finalizado" : "En curso")}" : contador;
    }

    private static bool SinEntrega(bool terminado, int entregados) => terminado && entregados == 0;
}

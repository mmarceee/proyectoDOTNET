using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Domain.Envios;

namespace Logistica.Modules.Envios.Application.Features.ConsultarEnviosApi;

internal enum EstadoResumenApi
{
    Creado,
    EnOperacion,
    EntregadoParcialmente,
    Entregado,
}

// Adaptación del modelo actual al vocabulario del ADR-0007. Los resultados actuales son del envío entero.
// Al persistir estados por bulto, esta proyección y el filtro deben consumir el resumen del dominio.
internal static class ProyeccionEnvioApi
{
    public static EstadoResumenApi ResumenDe(EstadoEnvio estado) => estado switch
    {
        EstadoEnvio.Admitido or EstadoEnvio.Cancelado => EstadoResumenApi.Creado,
        EstadoEnvio.Entregado => EstadoResumenApi.Entregado,
        _ => EstadoResumenApi.EnOperacion,
    };

    public static string? ResultadoBulto(EstadoEnvio estado) => estado switch
    {
        EstadoEnvio.Entregado => "Entregado",
        EstadoEnvio.Devuelto => "Devuelto",
        EstadoEnvio.Extraviado => "Extraviado",
        EstadoEnvio.Cancelado => "Cancelado",
        _ => null,
    };

    public static ResumenEnvioApiResponse Resumen(string numero, string? referencia, DateTimeOffset creadoEn,
        decimal tarifa, Modalidad modalidad, EstadoEnvio estado, int total)
    {
        var terminado = ResultadoBulto(estado) is not null;
        return new(numero, referencia, creadoEn, tarifa, modalidad.ToString(), ResumenDe(estado).ToString(), terminado,
            total, estado == EstadoEnvio.Entregado ? total : 0,
            new(terminado ? 0 : total,
                estado == EstadoEnvio.Devuelto ? total : 0,
                estado == EstadoEnvio.Extraviado ? total : 0,
                estado == EstadoEnvio.Cancelado ? total : 0));
    }

    public static EventoEnvioApiResponse Evento(EventoEnvio evento) => new(
        (evento.EstadoAnterior, evento.EstadoNuevo) switch
        {
            (_, EstadoEnvio.Admitido) => "Admision",
            (EstadoEnvio.AsignadoARuta, EstadoEnvio.EnDeposito) => "DesasignacionDeRuta",
            (EstadoEnvio.EnTransito, EstadoEnvio.EnDeposito) => "ReintegroADeposito",
            (_, EstadoEnvio.EnDeposito) => "RecepcionEnDeposito",
            (_, EstadoEnvio.AsignadoARuta) => "AsignacionARuta",
            (_, EstadoEnvio.EnTransito) => "SalidaEnReparto",
            (_, EstadoEnvio.Entregado) => "Entrega",
            (_, EstadoEnvio.NoEntregado) => "IntentoFallido",
            (_, EstadoEnvio.Reprogramado) => "Reprogramacion",
            (_, EstadoEnvio.EnDevolucion) => "InicioDevolucion",
            (_, EstadoEnvio.Devuelto) => "Devolucion",
            (_, EstadoEnvio.Extraviado) => "Extravio",
            (_, EstadoEnvio.Cancelado) => "Cancelacion",
            _ => "Actualizacion",
        }, evento.OcurridoEn);
}

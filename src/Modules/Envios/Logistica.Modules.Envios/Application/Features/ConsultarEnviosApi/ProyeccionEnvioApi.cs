using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Domain.Envios;

namespace Logistica.Modules.Envios.Application.Features.ConsultarEnviosApi;

internal static class ProyeccionEnvioApi
{
    public static ResumenEnvioApiResponse Resumen(string numero, string? referencia, DateTimeOffset creadoEn,
        decimal tarifa, Modalidad modalidad, ResumenEnvio resumen, bool terminado, int total, int entregados,
        int devueltos, int extraviados, int cancelados) =>
        new(numero, referencia, creadoEn, tarifa, modalidad.ToString(), resumen.ToString(), terminado, total, entregados,
            new(total - entregados - devueltos - extraviados - cancelados, devueltos, extraviados, cancelados));

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

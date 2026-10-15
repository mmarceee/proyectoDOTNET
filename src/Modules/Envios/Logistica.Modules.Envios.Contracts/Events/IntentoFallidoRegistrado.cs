namespace Logistica.Modules.Envios.Contracts.Events;

// Evento de integración (ADR-0003, sección 2.2): un intento de entrega falló (CU-18, T7) y el envío quedó Reprogramado
// (T9) o EnDevolucion (T10), según EstadoFinal. Lo espera Seguimiento, para avisar al destinatario y al comercio.
public sealed record IntentoFallidoRegistrado(Guid EnvioId, string Numero, int NumeroIntento, string Motivo,
    string EstadoFinal, DateTimeOffset OcurridoEn, DateTimeOffset? ReintentoDesde)
{
    public const string Tipo = "IntentoFallidoRegistrado.v1";
}

namespace Logistica.Modules.Envios.Contracts.Events;

// Evento de integración (ADR-0003, sección 2.2): el comercio creó el envío y quedó Admitido (CU-10, T1). Se publica desde
// la sesión del comercio, así el sobre lleva su ComercioId: Seguimiento lo usa para saber a qué comercio avisar (CU-64).
public sealed record EnvioAdmitido(Guid EnvioId, string Numero, DateTimeOffset OcurridoEn)
{
    public const string Tipo = "EnvioAdmitido.v1";
}

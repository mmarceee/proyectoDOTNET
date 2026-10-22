namespace Logistica.Modules.Administracion.Contracts.Events;

// Evento de integración (ADR-0003, sección 2.2): el administrador guardó la identidad visual de su operador (CU-07).
// Seguimiento guarda una copia para los correos al destinatario (CU-62): el Worker no consulta Administración.
// El operador viaja en el sobre; el logo no se incluye, igual que en el seguimiento público (CU-60).
public sealed record IdentidadVisualActualizada(string NombreOperador, string ColorPrimario, string ColorSecundario,
    string? EmailContacto, string? TelefonoContacto)
{
    public const string Tipo = "IdentidadVisualActualizada.v1";
}

namespace Logistica.Http.Contracts.Seguimiento;

// CU-61, paso 2: las fechas y franjas que el destinatario puede elegir desde su enlace. Texto viene listo para mostrar
// ("miércoles 29 de octubre, de 14:00 a 18:00"); vacía si el envío ya no se puede reprogramar.
public sealed record OpcionesReprogramacionResponse(IReadOnlyList<OpcionReprogramacionResponse> Opciones);

public sealed record OpcionReprogramacionResponse(DateOnly Fecha, Guid FranjaId, string Texto);

// CU-61, paso 3: cuerpo de POST /api/seguimiento/{token}/reprogramacion.
public sealed record SolicitarReprogramacionRequest(DateOnly Fecha, Guid FranjaId);

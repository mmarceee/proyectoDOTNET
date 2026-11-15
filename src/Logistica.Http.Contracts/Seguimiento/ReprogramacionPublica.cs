namespace Logistica.Http.Contracts.Seguimiento;

// CU-61, paso 2: las fechas y franjas que el destinatario puede elegir desde su enlace. Texto viene listo para mostrar
// ("miércoles 29 de octubre, de 14:00 a 18:00"); vacía si el envío ya no se puede reprogramar.
public sealed record OpcionesReprogramacionResponse(IReadOnlyList<OpcionReprogramacionResponse> Opciones);

public sealed record OpcionReprogramacionResponse(DateOnly Fecha, Guid FranjaId, string Texto);

// CU-61, paso 3: cuerpo de POST /api/seguimiento/{token}/reprogramacion/verificacion. Pide comprobar que quien reprograma
// es el destinatario, para esa fecha y franja.
public sealed record SolicitarVerificacionRequest(DateOnly Fecha, Guid FranjaId);

// Medio: "Correo" (se mandó un código de 6 dígitos a Destino, que viene enmascarado, por ejemplo a****@gmail.com) o
// "Telefono" (el envío no tiene correo: hay que escribir los últimos 4 dígitos del teléfono; Destino es null).
public sealed record VerificacionReprogramacionResponse(string Medio, string? Destino);

// CU-61, paso 4: cuerpo de POST /api/seguimiento/{token}/reprogramacion. Codigo es el que llegó al correo o los últimos
// 4 dígitos del teléfono.
public sealed record SolicitarReprogramacionRequest(DateOnly Fecha, Guid FranjaId, string Codigo);

namespace Logistica.Http.Contracts.Envios;

// Cuerpo de POST /api/envios (CU-10). Lo usan el endpoint y el Portal del comercio.
// FechaEntrega y FranjaHorariaId son opcionales y van juntas: el día y la franja horaria de la zona en que el comercio
// quiere la entrega. Sin ellas, el despachador lo planifica cualquier día.
public sealed record CrearEnvioRequest(
    DestinatarioRequest Destinatario,
    DireccionRequest Direccion,
    IReadOnlyList<BultoRequest> Bultos,
    string Modalidad = "Estandar",
    DateOnly? FechaEntrega = null,
    Guid? FranjaHorariaId = null);

// GET /api/envios/franjas: las franjas de la zona del código postal que rigen ese día, para elegir en el alta.
public sealed record FranjaDisponibleResponse(Guid Id, TimeOnly Desde, TimeOnly Hasta);

public sealed record DestinatarioRequest(string Nombre, string Telefono, string? Email, string? Documento);

public sealed record DireccionRequest(
    string Calle,
    string Numero,
    string Localidad,
    string Departamento,
    string CodigoPostal,
    string? Referencia);

public sealed record BultoRequest(decimal PesoKg, decimal LargoCm, decimal AnchoCm, decimal AltoCm);

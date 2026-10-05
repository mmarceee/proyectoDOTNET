namespace Logistica.Http.Contracts.Envios;

// Cuerpo de POST /api/envios (CU-10). Lo usan el endpoint y el Portal del comercio.
public sealed record CrearEnvioRequest(
    DestinatarioRequest Destinatario,
    DireccionRequest Direccion,
    IReadOnlyList<BultoRequest> Bultos);

public sealed record DestinatarioRequest(string Nombre, string Telefono, string? Email, string? Documento);

public sealed record DireccionRequest(
    string Calle,
    string Numero,
    string Localidad,
    string Departamento,
    string CodigoPostal,
    string? Referencia);

public sealed record BultoRequest(decimal PesoKg, decimal LargoCm, decimal AnchoCm, decimal AltoCm);

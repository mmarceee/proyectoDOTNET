namespace Logistica.Http.Contracts.Seguimiento;

// GET /api/seguimiento/{token} (CU-60): lo que ve la destinataria, con la marca del operador. Sin identificadores
// internos ni datos de terceros: el número del envío es el que figura en su comprobante.
public sealed record SeguimientoPublicoResponse(
    string Numero,
    string Estado,
    string NombreDePila,
    string Localidad,
    string? VentanaEstimada,
    bool PuedeReprogramar,
    IReadOnlyList<HitoSeguimientoResponse> Historial,
    MarcaSeguimientoResponse Marca);

public sealed record HitoSeguimientoResponse(string Estado, DateTimeOffset OcurridoEn);

public sealed record MarcaSeguimientoResponse(string Operador, string ColorPrimario, string ColorSecundario,
    string? EmailContacto, string? TelefonoContacto);

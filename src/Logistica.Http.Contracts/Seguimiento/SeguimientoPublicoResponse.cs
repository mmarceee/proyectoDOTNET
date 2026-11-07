namespace Logistica.Http.Contracts.Seguimiento;

// GET /api/seguimiento/{token} (CU-60): lo que ve la destinataria, con la marca del operador. Sin identificadores
// internos ni datos de terceros: el número del envío es el que figura en su comprobante.
// RF 35: Avance ("2 de 3 bultos entregados", null con un solo bulto) y cada bulto con su nombre ("BTO 1") y su estado.
public sealed record SeguimientoPublicoResponse(
    string Numero,
    string Estado,
    string NombreDePila,
    string Localidad,
    string? VentanaEstimada,
    bool PuedeReprogramar,
    IReadOnlyList<HitoSeguimientoResponse> Historial,
    MarcaSeguimientoResponse Marca,
    string? Avance,
    IReadOnlyList<BultoSeguimientoResponse> Bultos);

public sealed record HitoSeguimientoResponse(string Estado, DateTimeOffset OcurridoEn);

public sealed record MarcaSeguimientoResponse(string Operador, string ColorPrimario, string ColorSecundario,
    string? EmailContacto, string? TelefonoContacto);

// RF 35: un bulto del envío ("BTO 1"), sin Id ni código de etiqueta, y su estado en palabras de la destinataria.
public sealed record BultoSeguimientoResponse(string Nombre, string Estado);

namespace Logistica.Http.Contracts.Envios;

public sealed record CrearEnvioApiRequest(string ReferenciaComercio, DestinatarioRequest Destinatario,
    DireccionRequest Direccion, IReadOnlyList<BultoRequest> Bultos, string Modalidad = "Estandar");

public sealed record AltaEnvioApiResponse(string Numero, string ReferenciaComercio, decimal Tarifa,
    string Estado, string EnlaceSeguimiento);

namespace Logistica.Http.Contracts.ClavesApi;

public sealed record CrearClaveApiRequest(string Nombre, string Tipo);

public sealed record ClaveApiResponse(Guid Id, string Nombre, string Tipo, string Prefijo,
    DateTimeOffset CreadaEn, DateTimeOffset? UltimoUsoEn, DateTimeOffset? RevocadaEn);

public sealed record ClaveApiCreadaResponse(ClaveApiResponse Clave, string Secreto);

public sealed record TokenClaveApiResponse(string Token);

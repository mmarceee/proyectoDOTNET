namespace Logistica.Http.Contracts.Avisos;

public sealed record SuscripcionAvisoResponse(Guid Id, string UrlDestino, IReadOnlyList<string> TiposEvento, bool Activa);

namespace Logistica.Http.Contracts.Avisos;

public sealed record CrearSuscripcionAvisoRequest(string UrlDestino, IReadOnlyList<string> TiposEvento);

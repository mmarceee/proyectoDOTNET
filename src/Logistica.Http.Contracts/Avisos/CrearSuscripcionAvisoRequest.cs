namespace Logistica.Http.Contracts.Avisos;

// RF 36: Version 1 recibe los hechos del envío, como siempre; 2 recibe además los de cada bulto. Sin indicarla, 1.
public sealed record CrearSuscripcionAvisoRequest(string UrlDestino, IReadOnlyList<string> TiposEvento, int Version = 1);

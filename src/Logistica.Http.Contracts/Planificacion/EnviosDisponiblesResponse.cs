namespace Logistica.Http.Contracts.Planificacion;

public sealed record EnviosDisponiblesResponse(IReadOnlyList<EnvioDisponibleResponse> Items, int Total, int Pagina, int TamanoPagina);

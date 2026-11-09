namespace Logistica.Modules.Envios.Contracts.Results;

// CU-54 y CU-55 (RF 31): un envío de la ruta con todos sus bultos y el estado de cada uno. A diferencia de la carga, no
// filtra los bultos: la rendición decide cuáles pertenecen a la ruta.
public sealed record EnvioParaRendicion(Guid Id, string Numero, string Estado, IReadOnlyList<BultoParaRendicion> Bultos);
public sealed record BultoParaRendicion(Guid Id, string Codigo, string Estado);

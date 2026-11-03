namespace Logistica.Modules.Administracion.Contracts.Results;

// CU-32: un comercio del operador, para elegir a quién liquidar.
public sealed record ComercioRelacionado(Guid ComercioId, string RazonSocial);

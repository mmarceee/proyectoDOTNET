namespace Logistica.Modules.Administracion.Contracts.Results;

public sealed record ZonaPlanificacion(Guid Id, string Nombre, IReadOnlyList<string> CodigosPostales);

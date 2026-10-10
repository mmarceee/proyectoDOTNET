namespace Logistica.Http.Contracts.Planificacion;

public sealed record RestriccionRutaResponse(string Codigo, string Mensaje, Guid? EnvioId = null, Guid? BultoId = null);

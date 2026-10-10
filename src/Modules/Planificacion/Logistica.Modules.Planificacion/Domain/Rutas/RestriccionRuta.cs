namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal sealed record RestriccionRuta(string Codigo, string Mensaje, Guid? EnvioId = null, Guid? BultoId = null);

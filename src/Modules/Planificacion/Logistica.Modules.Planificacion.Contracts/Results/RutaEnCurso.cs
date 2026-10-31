namespace Logistica.Modules.Planificacion.Contracts.Results;

// CU-66: una ruta que está en la calle (Despachada o EnCurso), para el tablero en vivo. Los envíos sirven para calcular
// su avance con los estados que da Envíos; el vehículo, para ubicar su última posición (CU-53).
public sealed record RutaEnCurso(Guid RutaId, DateOnly Fecha, string Estado, Guid VehiculoId, Guid RepartidorId,
    IReadOnlyList<Guid> EnvioIds);

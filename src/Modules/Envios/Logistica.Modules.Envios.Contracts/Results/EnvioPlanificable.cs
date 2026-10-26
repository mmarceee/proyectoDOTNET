namespace Logistica.Modules.Envios.Contracts.Results;

// ReintentoDesde: después de un intento fallido reprogramado (CU-18, T9), el envío no se puede volver a intentar antes
// de ese instante. Lo calcula Envíos; Planificación lo compara con el comienzo de la franja (o del día, sin franja)
// al armar y al despachar la ruta.
public sealed record EnvioPlanificable(Guid Id, Guid OperadorId, string Numero, string Estado, Guid? ZonaId,
    Guid? FranjaHorariaId, DateOnly? FechaEntregaProgramada, string Direccion, string CodigoPostal,
    IReadOnlyList<BultoPlanificable> Bultos, PuntoEntregaPlanificable? Punto = null, DateTimeOffset? ReintentoDesde = null,
    string Modalidad = "Estandar");

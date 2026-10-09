namespace Logistica.Http.Contracts.Planificacion;

public sealed record CrearRutaRequest(DateOnly Fecha, Guid RepartidorId, Guid VehiculoId, IReadOnlyList<Guid> EnvioIds);
public sealed record ModificarRutaRequest(long RevisionEsperada, DateOnly Fecha, Guid RepartidorId, Guid VehiculoId);
public sealed record AgregarEnviosRutaRequest(long RevisionEsperada, IReadOnlyList<Guid> EnvioIds);
public sealed record PrevalidarRutaRequest(DateOnly Fecha, Guid RepartidorId, Guid VehiculoId, IReadOnlyList<Guid> EnvioIds,
    Guid? RutaId = null, long? RevisionEsperada = null);
public sealed record RestriccionRutaResponse(string Codigo, string Mensaje, Guid? EnvioId = null, Guid? BultoId = null);
public sealed record EvaluacionRutaResponse(bool Valida, decimal PesoKg, decimal VolumenM3, int Paradas,
    decimal CapacidadPesoKg, decimal CapacidadVolumenM3, int MaxParadas,
    IReadOnlyList<RestriccionRutaResponse> Restricciones);
public sealed record EnvioDisponibleResponse(Guid Id, string Numero, string Direccion, Guid? ZonaId, Guid? FranjaId,
    DateOnly? FechaProgramada, decimal PesoKg, decimal VolumenM3);
public sealed record EnviosDisponiblesResponse(IReadOnlyList<EnvioDisponibleResponse> Items, int Total, int Pagina, int TamanoPagina);
public sealed record ParadaRutaResponse(Guid Id, Guid EnvioId, int Orden, string Estado);
public sealed record RutaResponse(Guid Id, DateOnly Fecha, Guid RepartidorId, Guid VehiculoId, string Estado,
    long Revision, bool ReservaActiva, IReadOnlyList<ParadaRutaResponse> Paradas, EvaluacionRutaResponse? Carga = null);
public sealed record BultoValidadoResponse(Guid EnvioId, Guid BultoId, decimal PesoKg, decimal LargoCm,
    decimal AnchoCm, decimal AltoCm, decimal VolumenM3);
public sealed record ValidacionRutaResponse(Guid Id, long RevisionRuta, DateTimeOffset ValidadaEn, Guid? ResponsableId,
    string Operacion, DateOnly FechaRuta, Guid RepartidorId, Guid VehiculoId, Guid VersionReglasId, int MaxParadas,
    decimal CapacidadPesoKg, decimal CapacidadVolumenM3, decimal LargoCargaCm, decimal AnchoCargaCm, decimal AltoCargaCm,
    IReadOnlyList<BultoValidadoResponse> Bultos);

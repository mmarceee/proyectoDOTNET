namespace Logistica.Http.Contracts.Repartidor;

public sealed record SesionRepartidorResponse(Guid UsuarioId, Guid OperadorId, Guid RepartidorId,
    DateOnly FechaOperativa, string ZonaHorariaOperador, DateTimeOffset VerificadaEn)
{
    public string? NombreUsuario { get; init; }
}

public sealed record HojaDeRutaResponse(int SchemaVersion, Guid UsuarioId, Guid OperadorId, Guid RepartidorId,
    Guid RutaId, long RevisionRuta, DateOnly FechaRuta, DateOnly FechaOperativa, string ZonaHorariaOperador,
    string EstadoRuta, DateTimeOffset GeneradaEn, VehiculoHojaResponse Vehiculo,
    IReadOnlyList<ZonaHojaResponse> Zonas, IReadOnlyList<ParadaHojaResponse> Paradas);

public sealed record VehiculoHojaResponse(Guid Id, string Matricula);
public sealed record ZonaHojaResponse(Guid Id, string Codigo, string Nombre);
public sealed record ParadaHojaResponse(Guid Id, int Orden, string Estado, string Direccion,
    TimeOnly? Desde, TimeOnly? Hasta, IReadOnlyList<EnvioHojaResponse> Envios);
public sealed record EnvioHojaResponse(Guid Id, string Numero, string Estado, string DestinatarioNombre,
    string DestinatarioTelefono, DireccionHojaResponse Direccion, FranjaHojaResponse? Franja,
    IReadOnlyList<BultoHojaResponse> Bultos, ReglasEntregaHojaResponse ReglasEntrega);
public sealed record DireccionHojaResponse(string Calle, string Numero, string Localidad, string Departamento,
    string CodigoPostal, string? Referencia);
public sealed record FranjaHojaResponse(Guid Id, TimeOnly Desde, TimeOnly Hasta);
public sealed record BultoHojaResponse(Guid Id, string Codigo);
public sealed record ReglasEntregaHojaResponse(string Origen, Guid? VersionId, bool FirmaObligatoria,
    bool ExigeFoto, bool ExigeNombreYDocumento, int MaxIntentos, TimeSpan PlazoEntreIntentos,
    IReadOnlyList<MotivoHojaResponse> MotivosNoEntrega);
public sealed record MotivoHojaResponse(Guid Id, string Codigo, string NombreParaMostrar, bool Reprogramable, bool ExigeEvidencia);

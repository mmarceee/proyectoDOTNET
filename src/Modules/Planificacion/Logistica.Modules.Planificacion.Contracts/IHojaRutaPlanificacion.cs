namespace Logistica.Modules.Planificacion.Contracts;

public interface IHojaRutaPlanificacion
{
    Task<RutaParaHoja?> ConsultarAsync(Guid repartidorId, DateOnly fecha, CancellationToken ct);
}
public sealed record RutaParaHoja(Guid Id, Guid VehiculoId, long Revision, DateOnly Fecha, string Estado,
    IReadOnlyList<ParadaParaHoja> Paradas);
public sealed record ParadaParaHoja(Guid Id, int Orden, string Estado, string Direccion,
    TimeOnly? Desde, TimeOnly? Hasta, IReadOnlyList<Guid> Envios);

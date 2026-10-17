namespace Logistica.Modules.Administracion.Contracts;

public interface IContextoHojaRuta
{
    Task<ContextoHojaRuta?> ConsultarAsync(Guid usuarioId, Guid repartidorId, Guid? vehiculoId, CancellationToken ct);
}
public sealed record ContextoHojaRuta(Guid OperadorId, string ZonaHoraria, string? Matricula,
    IReadOnlyList<ZonaHojaRuta> Zonas, IReadOnlyList<FranjaHojaRuta> Franjas);
public sealed record ZonaHojaRuta(Guid Id, string Codigo, string Nombre, IReadOnlyList<string> CodigosPostales);
public sealed record FranjaHojaRuta(Guid Id, Guid? ReemplazaAId, Guid ZonaId, TimeOnly Desde, TimeOnly Hasta,
    DateOnly VigenteDesde, DateOnly? VigenteHasta, IReadOnlyList<DayOfWeek> Dias);

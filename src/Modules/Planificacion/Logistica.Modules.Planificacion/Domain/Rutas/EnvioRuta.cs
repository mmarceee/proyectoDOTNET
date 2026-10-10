namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal sealed record EnvioRuta(Guid Id, string Numero, DateOnly? Fecha, Guid? ZonaId, FranjaRuta? Franja,
    bool FranjaRequerida, IReadOnlyList<CargaBulto> Bultos);

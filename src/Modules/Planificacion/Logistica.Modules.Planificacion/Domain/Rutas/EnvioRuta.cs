namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal sealed record EnvioRuta(Guid Id, string Numero, DateOnly? Fecha, Guid? ZonaId, FranjaRuta? Franja,
    bool FranjaRequerida, IReadOnlyList<CargaBulto> Bultos, PuntoEntrega? Punto = null,
    DateTimeOffset? ReintentoDesde = null, DateTimeOffset? InicioPrevisto = null)
{
    public bool CumplePlazoEntreIntentos => ReintentoDesde is null
        || (InicioPrevisto is DateTimeOffset inicio && inicio >= ReintentoDesde.Value);
}

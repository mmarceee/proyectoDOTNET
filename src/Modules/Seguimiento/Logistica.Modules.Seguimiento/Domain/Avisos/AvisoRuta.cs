using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Domain.Avisos;

internal sealed class AvisoRuta : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid RepartidorId { get; private set; }
    public Guid RutaId { get; private set; }
    public DateOnly FechaRuta { get; private set; }
    public DateTimeOffset DespachadaEn { get; private set; }
    public long Revision { get; private set; }
    private AvisoRuta() { }

    public static AvisoRuta Crear(Guid operadorId, Guid repartidorId, Guid rutaId, DateOnly fecha,
        long revision, DateTimeOffset despachadaEn)
    {
        if (operadorId == Guid.Empty || repartidorId == Guid.Empty || rutaId == Guid.Empty || revision < 1 || fecha == default)
            throw new DomainException("El aviso necesita operador, repartidor, ruta, fecha y revisión válidos.");
        return new() { OperadorId = operadorId, RepartidorId = repartidorId, RutaId = rutaId,
            FechaRuta = fecha, Revision = revision, DespachadaEn = despachadaEn };
    }
}

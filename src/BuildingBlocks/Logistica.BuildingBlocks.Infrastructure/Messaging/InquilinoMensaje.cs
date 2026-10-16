using Logistica.SharedKernel;

namespace Logistica.BuildingBlocks.Infrastructure.Messaging;

// Se fija una sola vez por ámbito, antes de resolver el consumidor y sus DbContext.
public sealed class InquilinoMensaje : ICurrentTenant
{
    public bool Activo { get; private set; }
    public Guid? OperadorId { get; private set; }
    public Guid? ComercioId { get; private set; }

    public void Fijar(MensajeIntegracion mensaje)
    {
        if (Activo || mensaje.OperadorId == Guid.Empty)
            throw new InvalidDataException("El mensaje necesita un operador válido y un ámbito nuevo.");
        OperadorId = mensaje.OperadorId;
        ComercioId = mensaje.ComercioId;
        Activo = true;
    }

    public void SinInquilino() => Activo = true;
}

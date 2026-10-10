using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal sealed class Parada : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid RutaId { get; private set; }
    public Guid EnvioId { get; private set; }
    public int Orden { get; private set; }
    public EstadoParada Estado { get; private set; }
    public DateTimeOffset? LlegadaEn { get; private set; }
    private Parada() { }
    internal Parada(Ruta ruta, Guid envioId, int orden)
    { OperadorId = ruta.OperadorId; RutaId = ruta.Id; EnvioId = envioId; Orden = orden; Estado = EstadoParada.Pendiente; }
    internal void CambiarOrden(int orden) => Orden = orden;
}

using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal sealed class EnvioParada : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid RutaId { get; private set; }
    public Guid ParadaId { get; private set; }
    public Guid EnvioId { get; private set; }
    public EstadoParada Estado { get; private set; }
    private EnvioParada() { }
    internal EnvioParada(Parada parada, Guid envioId)
    { OperadorId = parada.OperadorId; RutaId = parada.RutaId; ParadaId = parada.Id; EnvioId = envioId; Estado = EstadoParada.Pendiente; }
    internal void MoverA(Parada parada) => ParadaId = parada.Id;
    internal void Cerrar(bool entregado) => Estado = entregado ? EstadoParada.Completada : EstadoParada.Fallida;
}

using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Planificacion;

internal sealed class VersionReglasPlanificacion : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public int Numero { get; private set; }
    public DateTimeOffset VigenteDesde { get; private set; }
    public DateTimeOffset? VigenteHasta { get; private set; }
    public int MaxParadasPorRuta { get; private set; }
    private VersionReglasPlanificacion() { }
    private VersionReglasPlanificacion(Guid id) : base(id) { }
    public static VersionReglasPlanificacion Publicar(Guid operador, int numero, int maxParadas, DateTimeOffset desde, Guid? id = null)
    {
        if (operador == Guid.Empty || numero < 1 || maxParadas < 1) throw new DomainException("La versión y el máximo de paradas deben ser positivos.");
        return new(id ?? Guid.CreateVersion7()) { OperadorId = operador, Numero = numero, MaxParadasPorRuta = maxParadas, VigenteDesde = desde };
    }
}

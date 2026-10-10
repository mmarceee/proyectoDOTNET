using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Planificacion;

internal sealed class Repartidor : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public string Nombre { get; private set; } = "";
    public string Documento { get; private set; } = "";
    public bool Activo { get; private set; }
    private Repartidor() { }
    private Repartidor(Guid id) : base(id) { }
    public static Repartidor Crear(Guid operador, string nombre, string documento, Guid? id = null)
    {
        if (operador == Guid.Empty || string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(documento))
            throw new DomainException("El repartidor necesita operador, nombre y documento.");
        return new(id ?? Guid.CreateVersion7()) { OperadorId = operador, Nombre = nombre.Trim(), Documento = documento.Trim(), Activo = true };
    }
}

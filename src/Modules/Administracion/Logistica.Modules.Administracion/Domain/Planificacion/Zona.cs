using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Planificacion;

internal sealed class Zona : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public string Codigo { get; private set; } = "";
    public string Nombre { get; private set; } = "";
    public string[] CodigosPostales { get; private set; } = [];
    public bool Activa { get; private set; }
    private Zona() { }
    private Zona(Guid id) : base(id) { }
    public static Zona Crear(Guid operador, string codigo, string nombre, string[] postales, Guid? id = null)
    {
        if (operador == Guid.Empty || string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(nombre)
            || postales.Length == 0 || postales.Any(string.IsNullOrWhiteSpace)) throw new DomainException("La zona necesita código, nombre y cobertura.");
        return new(id ?? Guid.CreateVersion7()) { OperadorId = operador, Codigo = codigo.Trim(), Nombre = nombre.Trim(),
            CodigosPostales = postales.Select(p => p.Trim()).Distinct().ToArray(), Activa = true };
    }
}

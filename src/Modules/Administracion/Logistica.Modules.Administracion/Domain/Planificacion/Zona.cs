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
        if (operador == Guid.Empty || string.IsNullOrWhiteSpace(codigo)) throw new DomainException("La zona necesita código, nombre y cobertura.");
        // El código se guarda en mayúsculas: "mvd-1" y "MVD-1" son la misma zona (A1).
        var zona = new Zona(id ?? Guid.CreateVersion7())
            { OperadorId = operador, Codigo = codigo.Trim().ToUpperInvariant(), Activa = true };
        zona.Modificar(nombre, postales);
        return zona;
    }

    // CU-04, A4: cambian el nombre y la cobertura; el código no. Los envíos ya admitidos conservan su zona.
    public void Modificar(string nombre, string[] postales)
    {
        if (string.IsNullOrWhiteSpace(nombre) || postales.Length == 0 || postales.Any(string.IsNullOrWhiteSpace))
            throw new DomainException("La zona necesita código, nombre y cobertura.");
        Nombre = nombre.Trim();
        CodigosPostales = postales.Select(p => p.Trim()).Distinct().ToArray();
    }

    // CU-04, A5: deja de aceptarse en envíos nuevos; los existentes no se modifican.
    public void Desactivar() => Activa = false;

    // ¿El código postal es de esta zona? Lo usa la regla de A2: cada código postal, en una sola zona activa.
    public bool Cubre(string codigoPostal) => CodigosPostales.Contains(codigoPostal.Trim());
}

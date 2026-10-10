using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Planificacion;

internal sealed class Vehiculo : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public string Matricula { get; private set; } = "";
    public string Tipo { get; private set; } = "";
    public decimal CapacidadPesoKg { get; private set; }
    public decimal CapacidadVolumenM3 { get; private set; }
    public decimal LargoCargaCm { get; private set; }
    public decimal AnchoCargaCm { get; private set; }
    public decimal AltoCargaCm { get; private set; }
    public bool Activo { get; private set; }
    private Vehiculo() { }
    private Vehiculo(Guid id) : base(id) { }
    public static Vehiculo Crear(Guid operador, string matricula, string tipo, decimal peso, decimal volumen,
        decimal largo, decimal ancho, decimal alto, Guid? id = null)
    {
        if (operador == Guid.Empty || string.IsNullOrWhiteSpace(matricula) || string.IsNullOrWhiteSpace(tipo)
            || new[] { peso, volumen, largo, ancho, alto }.Any(v => v <= 0))
            throw new DomainException("El vehículo necesita matrícula, tipo y capacidades positivas.");
        return new(id ?? Guid.CreateVersion7()) { OperadorId = operador, Matricula = matricula.Trim().ToUpperInvariant(),
            Tipo = tipo.Trim(), CapacidadPesoKg = peso, CapacidadVolumenM3 = volumen, LargoCargaCm = largo,
            AnchoCargaCm = ancho, AltoCargaCm = alto, Activo = true };
    }
}

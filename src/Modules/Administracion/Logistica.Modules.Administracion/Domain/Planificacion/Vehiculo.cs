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
        if (operador == Guid.Empty || string.IsNullOrWhiteSpace(matricula))
            throw new DomainException("El vehículo necesita matrícula, tipo y capacidades positivas.");
        // La matrícula se guarda en mayúsculas: "abc 1234" y "ABC 1234" son el mismo vehículo (A1).
        var vehiculo = new Vehiculo(id ?? Guid.CreateVersion7())
            { OperadorId = operador, Matricula = matricula.Trim().ToUpperInvariant(), Activo = true };
        vehiculo.Modificar(tipo, peso, volumen, largo, ancho, alto);
        return vehiculo;
    }

    // CU-08: cambian el tipo y las capacidades; la matrícula no. Las rutas ya validadas conservan su evidencia de carga.
    public void Modificar(string tipo, decimal peso, decimal volumen, decimal largo, decimal ancho, decimal alto)
    {
        // A2: toda capacidad y dimensión tiene que ser mayor que cero.
        if (string.IsNullOrWhiteSpace(tipo) || new[] { peso, volumen, largo, ancho, alto }.Any(v => v <= 0))
            throw new DomainException("El vehículo necesita matrícula, tipo y capacidades positivas.");
        Tipo = tipo.Trim();
        CapacidadPesoKg = peso;
        CapacidadVolumenM3 = volumen;
        LargoCargaCm = largo;
        AnchoCargaCm = ancho;
        AltoCargaCm = alto;
    }

    // CU-08: deja de ofrecerse para rutas nuevas; las rutas anteriores lo conservan. A3 la controla quien llama.
    public void Desactivar() => Activo = false;
}

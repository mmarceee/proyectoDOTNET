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
internal sealed class FranjaHoraria : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid ZonaId { get; private set; }
    public TimeOnly HoraDesde { get; private set; }
    public TimeOnly HoraHasta { get; private set; }
    public int[] Dias { get; private set; } = [];
    public DateOnly VigenteDesde { get; private set; }
    public DateOnly? VigenteHasta { get; private set; }
    public Guid? ReemplazaAId { get; private set; }
    private FranjaHoraria() { }
    private FranjaHoraria(Guid id) : base(id) { }
    public static FranjaHoraria Crear(Guid operador, Guid zona, TimeOnly desde, TimeOnly hasta, DayOfWeek[] dias,
        DateOnly vigenteDesde, Guid? id = null)
    {
        if (operador == Guid.Empty || zona == Guid.Empty || desde >= hasta || dias.Length == 0
            || dias.Any(d => !Enum.IsDefined(d))) throw new DomainException("La franja debe tener zona, horario y días válidos.");
        return new(id ?? Guid.CreateVersion7()) { OperadorId = operador, ZonaId = zona, HoraDesde = desde,
            HoraHasta = hasta, Dias = dias.Select(d => (int)d).Distinct().ToArray(), VigenteDesde = vigenteDesde };
    }
    public FranjaHoraria Reemplazar(TimeOnly desde, TimeOnly hasta, DayOfWeek[] dias, DateOnly fecha)
    {
        if (VigenteHasta is not null || fecha <= VigenteDesde) throw new DomainException("Debe reemplazarse la última franja con una vigencia posterior.");
        var nueva = Crear(OperadorId, ZonaId, desde, hasta, dias, fecha);
        nueva.ReemplazaAId = Id; VigenteHasta = fecha.AddDays(-1);
        return nueva;
    }
    public bool Aplica(DateOnly fecha) => fecha >= VigenteDesde && (VigenteHasta is null || fecha <= VigenteHasta)
        && Dias.Contains((int)fecha.DayOfWeek);
}
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

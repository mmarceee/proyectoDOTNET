using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Planificacion;

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

using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal sealed class BultoValidacionRuta : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid ValidacionRutaId { get; private set; }
    public Guid EnvioId { get; private set; }
    public Guid BultoId { get; private set; }
    public decimal PesoKg { get; private set; }
    public decimal LargoCm { get; private set; }
    public decimal AnchoCm { get; private set; }
    public decimal AltoCm { get; private set; }
    public decimal VolumenM3 { get; private set; }
    private BultoValidacionRuta() { }
    internal BultoValidacionRuta(ValidacionRuta validacion, CargaBulto bulto)
    { OperadorId = validacion.OperadorId; ValidacionRutaId = validacion.Id; EnvioId = bulto.EnvioId; BultoId = bulto.BultoId;
        PesoKg = bulto.PesoKg; LargoCm = bulto.LargoCm; AnchoCm = bulto.AnchoCm; AltoCm = bulto.AltoCm; VolumenM3 = bulto.VolumenM3; }
}

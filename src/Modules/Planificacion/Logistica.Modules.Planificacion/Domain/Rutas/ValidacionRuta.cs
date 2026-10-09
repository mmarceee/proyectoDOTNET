using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal sealed class ValidacionRuta : Entity, IOperadorOwned
{
    private readonly List<BultoValidacionRuta> _bultos = [];
    public Guid OperadorId { get; private set; }
    public Guid RutaId { get; private set; }
    public long RevisionRuta { get; private set; }
    public DateTimeOffset ValidadaEn { get; private set; }
    public Guid? ResponsableId { get; private set; }
    public OperacionValidacionRuta Operacion { get; private set; }
    public DateOnly FechaRuta { get; private set; }
    public Guid RepartidorId { get; private set; }
    public Guid VehiculoId { get; private set; }
    public decimal CapacidadPesoKg { get; private set; }
    public decimal CapacidadVolumenM3 { get; private set; }
    public decimal LargoCargaCm { get; private set; }
    public decimal AnchoCargaCm { get; private set; }
    public decimal AltoCargaCm { get; private set; }
    public Guid VersionReglasId { get; private set; }
    public int MaxParadasPorRuta { get; private set; }
    public IReadOnlyList<BultoValidacionRuta> Bultos => _bultos;
    private ValidacionRuta() { }
    public static ValidacionRuta Registrar(Ruta ruta, OperacionValidacionRuta operacion, VehiculoRuta vehiculo,
        Guid reglasId, int maxParadas, IReadOnlyList<EnvioRuta> envios, Guid? responsable, DateTimeOffset ahora)
    {
        var validacion = new ValidacionRuta { OperadorId = ruta.OperadorId, RutaId = ruta.Id, RevisionRuta = ruta.Revision,
            ValidadaEn = ahora, ResponsableId = responsable, Operacion = operacion, FechaRuta = ruta.Fecha,
            RepartidorId = ruta.RepartidorId, VehiculoId = vehiculo.Id, CapacidadPesoKg = vehiculo.PesoKg,
            CapacidadVolumenM3 = vehiculo.VolumenM3, LargoCargaCm = vehiculo.LargoCm, AnchoCargaCm = vehiculo.AnchoCm,
            AltoCargaCm = vehiculo.AltoCm, VersionReglasId = reglasId, MaxParadasPorRuta = maxParadas };
        foreach (var bulto in envios.SelectMany(e => e.Bultos)) validacion._bultos.Add(new(validacion, bulto));
        return validacion;
    }
}
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

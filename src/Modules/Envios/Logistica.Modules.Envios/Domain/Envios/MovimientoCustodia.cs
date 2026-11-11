using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

internal enum UbicacionCustodia { Desconocida, Deposito, Repartidor, Destinatario, Comercio }

// Hecho auditable, separado de las transiciones de estado. No se reescribe al reasignar.
internal sealed class MovimientoCustodia : Entity, IOperadorOwned, IComercioOwned
{
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public Guid BultoId { get; private set; }
    public Guid EnvioId { get; private set; }
    public UbicacionCustodia Ubicacion { get; private set; }
    public Guid? RutaId { get; private set; }
    public Guid? ResponsableId { get; private set; }
    public Guid OperacionId { get; private set; }
    public DateTimeOffset ConfirmadaEn { get; private set; }
    public string Motivo { get; private set; } = "";
    private MovimientoCustodia() { }
    internal MovimientoCustodia(Bulto bulto, UbicacionCustodia ubicacion, Guid? ruta, Guid? actor,
        Guid operacion, DateTimeOffset ahora, string motivo)
    {
        OperadorId = bulto.OperadorId; ComercioId = bulto.ComercioId; BultoId = bulto.Id; EnvioId = bulto.EnvioId;
        Ubicacion = ubicacion; RutaId = ruta; ResponsableId = actor; OperacionId = operacion;
        ConfirmadaEn = ahora.ToUniversalTime(); Motivo = motivo;
    }
}

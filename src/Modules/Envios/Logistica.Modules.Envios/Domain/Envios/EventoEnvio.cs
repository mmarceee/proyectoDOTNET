using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

// Historial del envío: sólo se insertan eventos, nunca se modifican (RF 12).
internal sealed class EventoEnvio : Entity, IOperadorOwned, IComercioOwned
{
    public Guid EnvioId { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public EstadoEnvio? EstadoAnterior { get; private set; }
    public EstadoEnvio EstadoNuevo { get; private set; }
    public DateTimeOffset OcurridoEn { get; private set; }
    public OrigenEvento Origen { get; private set; }
    public Guid? ResponsableId { get; private set; }

    private EventoEnvio() { } // para EF Core

    // Sólo lo llama Envio, al crearse y en cada transición.
    public EventoEnvio(Envio envio, EstadoEnvio? estadoAnterior, EstadoEnvio estadoNuevo,
        DateTimeOffset ocurridoEn, OrigenEvento origen, Guid? responsableId)
    {
        EnvioId = envio.Id;
        OperadorId = envio.OperadorId;
        ComercioId = envio.ComercioId;
        EstadoAnterior = estadoAnterior;
        EstadoNuevo = estadoNuevo;
        OcurridoEn = ocurridoEn;
        Origen = origen;
        ResponsableId = responsableId;
    }
}

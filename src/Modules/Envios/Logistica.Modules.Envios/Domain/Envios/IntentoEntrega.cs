using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

internal sealed class IntentoEntrega : Entity, IOperadorOwned, IComercioOwned
{
    public Guid EnvioId { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public int NumeroIntento { get; private set; }
    public DateTimeOffset FechaHora { get; private set; }
    public ResultadoIntento Resultado { get; private set; }
    public Guid? MotivoNoEntregaId { get; private set; }
    public PruebaEntrega? Evidencia { get; private set; }
    public string? Observaciones { get; private set; }

    private IntentoEntrega() { }

    public IntentoEntrega(Envio envio, int numero, DateTimeOffset fechaHora, ResultadoIntento resultado,
        Guid? motivoNoEntregaId, PruebaEntrega? evidencia, string? observaciones)
    {
        if (numero < 1 || !Enum.IsDefined(resultado))
        {
            throw new DomainException("El número y resultado del intento deben ser válidos.");
        }

        if (resultado == ResultadoIntento.Fallido && (motivoNoEntregaId is null || motivoNoEntregaId == Guid.Empty))
        {
            throw new DomainException("Un intento fallido necesita un motivo de no entrega.");
        }

        if (resultado == ResultadoIntento.Exitoso && (evidencia?.FirmaArchivoId is null || motivoNoEntregaId is not null))
        {
            throw new DomainException("Un intento exitoso necesita firma y no admite un motivo de no entrega.");
        }

        EnvioId = envio.Id;
        OperadorId = envio.OperadorId;
        ComercioId = envio.ComercioId;
        NumeroIntento = numero;
        FechaHora = fechaHora;
        Resultado = resultado;
        MotivoNoEntregaId = motivoNoEntregaId;
        Evidencia = evidencia;
        Observaciones = observaciones;
    }
}

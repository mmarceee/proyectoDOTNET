using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Domain.Avisos;

// Un aviso de un hecho del envío para una suscripción, con sus intentos (CU-64). El Id es el identificador que recibe
// el comercio: se conserva en todos los reintentos para que el receptor descarte duplicados.
internal sealed class EntregaAviso : Entity, IOperadorOwned, IComercioOwned
{
    private readonly List<IntentoAviso> _intentos = [];

    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public Guid SuscripcionAvisoId { get; private set; }
    // MessageId del evento de Envíos que originó el aviso. Con la suscripción, identifica la entrega.
    public Guid EventoId { get; private set; }
    public Guid EnvioId { get; private set; }
    public string NumeroEnvio { get; private set; } = "";
    public string EstadoEnvio { get; private set; } = "";
    public TipoEventoAviso Tipo { get; private set; }
    public DateTimeOffset FechaEvento { get; private set; }
    public EstadoEntregaAviso Estado { get; private set; }
    public bool EsPrueba { get; private set; }
    // RF 36: la versión de la suscripción al preparar el aviso y, en v2, el bulto del hecho. Se guardan con la entrega
    // para que los reintentos y los reenvíos manden el mismo contenido, aunque la suscripción cambie después.
    public int Version { get; private set; } = SuscripcionAviso.VersionInicial;
    public string? CodigoBulto { get; private set; }
    public string? EstadoBulto { get; private set; }
    public string? MotivoBulto { get; private set; }
    public IReadOnlyCollection<IntentoAviso> Intentos => _intentos.AsReadOnly();

    public BultoAviso? Bulto => CodigoBulto is null ? null : new(CodigoBulto, EstadoBulto!, MotivoBulto);

    private EntregaAviso() { }

    public static EntregaAviso Crear(SuscripcionAviso suscripcion, Guid eventoId, EnvioSeguido envio,
        string estadoEnvio, TipoEventoAviso tipo, DateTimeOffset fechaEvento, bool esPrueba = false,
        BultoAviso? bulto = null)
    {
        if (eventoId == Guid.Empty || string.IsNullOrWhiteSpace(estadoEnvio))
            throw new DomainException("La entrega del aviso necesita el evento y el estado del envío.");
        if (suscripcion.OperadorId != envio.OperadorId || suscripcion.ComercioId != envio.ComercioId)
            throw new DomainException("La suscripción no pertenece al comercio del envío.");
        if (!suscripcion.Recibe(tipo))
            throw new DomainException("La suscripción no recibe este tipo de evento.");
        if (bulto is not null && suscripcion.Version < SuscripcionAviso.VersionConBultos)
            throw new DomainException("Una suscripción v1 no recibe avisos de bulto.");
        if (bulto is not null && (string.IsNullOrWhiteSpace(bulto.Codigo) || string.IsNullOrWhiteSpace(bulto.Estado)
            || bulto.Codigo.Length > BultoAviso.LargoCodigo || bulto.Estado.Length > BultoAviso.LargoEstado
            || bulto.Motivo?.Length > BultoAviso.LargoMotivo))
            throw new DomainException("El bulto del aviso necesita un código y un estado válidos.");

        return new()
        {
            OperadorId = suscripcion.OperadorId,
            ComercioId = suscripcion.ComercioId,
            SuscripcionAvisoId = suscripcion.Id,
            EventoId = eventoId,
            EnvioId = envio.Id,
            NumeroEnvio = envio.Numero,
            EstadoEnvio = estadoEnvio,
            Tipo = tipo,
            FechaEvento = fechaEvento,
            Estado = EstadoEntregaAviso.Pendiente,
            EsPrueba = esPrueba,
            Version = suscripcion.Version,
            CodigoBulto = bulto?.Codigo,
            EstadoBulto = bulto?.Estado,
            MotivoBulto = bulto?.Motivo,
        };
    }

    // ultimo: la mensajería ya agotó los reintentos (ADR-0003, sección 2.6); si este intento falla, va a la cola de fallidos.
    public void RegistrarIntento(DateTimeOffset enviadoEn, bool exitoso, int? codigoRespuesta, string? error, bool ultimo)
    {
        if (Estado is EstadoEntregaAviso.Exitosa or EstadoEntregaAviso.EnColaDeFallidos)
            throw new DomainException("La entrega del aviso no admite más intentos.");

        _intentos.Add(new IntentoAviso(this, _intentos.Count + 1, enviadoEn, exitoso, codigoRespuesta, error));
        Estado = exitoso ? EstadoEntregaAviso.Exitosa
            : ultimo ? EstadoEntregaAviso.EnColaDeFallidos
            : EstadoEntregaAviso.Fallida;
    }

    // Reenvío de una entrega en la cola de fallidos (CU-65, o CU-09 al reintentar el mensaje): vuelve a esperar un intento.
    public void Reintentar()
    {
        if (Estado != EstadoEntregaAviso.EnColaDeFallidos)
            throw new DomainException("Sólo se reintenta una entrega que está en la cola de fallidos.");
        Estado = EstadoEntregaAviso.Pendiente;
    }
}

internal sealed class IntentoAviso : Entity, IOperadorOwned, IComercioOwned
{
    public const int LargoError = 500;

    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public Guid EntregaAvisoId { get; private set; }
    public int Numero { get; private set; }
    public DateTimeOffset EnviadoEn { get; private set; }
    public bool Exitoso { get; private set; }
    public int? CodigoRespuesta { get; private set; }
    public string? Error { get; private set; }

    private IntentoAviso() { }

    internal IntentoAviso(EntregaAviso entrega, int numero, DateTimeOffset enviadoEn, bool exitoso,
        int? codigoRespuesta, string? error)
    {
        OperadorId = entrega.OperadorId;
        ComercioId = entrega.ComercioId;
        EntregaAvisoId = entrega.Id;
        Numero = numero;
        EnviadoEn = enviadoEn;
        Exitoso = exitoso;
        CodigoRespuesta = codigoRespuesta;
        Error = error is { Length: > LargoError } ? error[..LargoError] : error;
    }
}

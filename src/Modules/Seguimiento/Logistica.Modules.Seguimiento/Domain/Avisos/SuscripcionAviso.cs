using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Domain.Avisos;

internal sealed class SuscripcionAviso : Entity, IOperadorOwned, IComercioOwned
{
    // RF 36. v1: los hechos del envío, como hasta RF 31. v2: además, los hechos de cada bulto.
    public const int VersionInicial = 1;
    public const int VersionConBultos = 2;

    private readonly List<TipoEventoAviso> _tiposEvento = [];

    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public Guid RelacionComercialId { get; private set; }
    public string UrlDestino { get; private set; } = "";
    public IReadOnlyCollection<TipoEventoAviso> TiposEvento => _tiposEvento.AsReadOnly();
    // La aplicación entrega el valor ya cifrado; el dominio no conoce el proveedor de cifrado.
    public string SecretoFirma { get; private set; } = "";
    public bool Activa { get; private set; }
    // La versión del contrato que recibe el receptor. Se elige al crear la suscripción y no cambia: para pasar a v2,
    // el comercio crea otra suscripción y desactiva la anterior cuando su sistema ya procesa la nueva.
    public int Version { get; private set; } = VersionInicial;

    private SuscripcionAviso() { }

    public static SuscripcionAviso Crear(Guid operadorId, Guid comercioId, Guid relacionComercialId,
        string urlDestino, IReadOnlyCollection<TipoEventoAviso> tiposEvento, string secretoFirmaCifrado,
        int version = VersionInicial)
    {
        if (operadorId == Guid.Empty || comercioId == Guid.Empty || relacionComercialId == Guid.Empty)
            throw new DomainException("La suscripción necesita operador, comercio y relación comercial válidos.");

        var direccion = urlDestino?.Trim() ?? "";
        if (!Uri.TryCreate(direccion, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host))
            throw new DomainException("La dirección del aviso debe ser una URL HTTPS válida.");

        if (tiposEvento is null || tiposEvento.Count == 0 || tiposEvento.Any(t => !Enum.IsDefined(t)))
            throw new DomainException("La suscripción necesita al menos un tipo de evento válido.");

        if (version is not (VersionInicial or VersionConBultos))
            throw new DomainException("Las versiones de aviso válidas son 1 y 2.");

        ValidarSecreto(secretoFirmaCifrado);

        var suscripcion = new SuscripcionAviso
        {
            OperadorId = operadorId,
            ComercioId = comercioId,
            RelacionComercialId = relacionComercialId,
            UrlDestino = direccion,
            SecretoFirma = secretoFirmaCifrado,
            Activa = true,
            Version = version,
        };
        suscripcion._tiposEvento.AddRange(tiposEvento.Distinct());
        return suscripcion;
    }

    public void Desactivar() => Activa = false;

    // CU-64: CambioDeEstado abarca todos los hechos del envío; EntregaFallida y Devolucion, sólo los suyos.
    public bool Recibe(TipoEventoAviso tipo) =>
        Activa && (_tiposEvento.Contains(TipoEventoAviso.CambioDeEstado) || _tiposEvento.Contains(tipo));

    // RF 36: una suscripción v1 no recibe hechos de bulto ni un cierre mixto (devuelto con bultos ya entregados), que
    // le haría creer que no se entregó nada. Para verlos, el comercio pasa a v2.
    public bool Recibe(TipoEventoAviso tipo, bool esDeBulto, bool cierreMixto) =>
        Recibe(tipo) && (Version >= VersionConBultos || !esDeBulto && !cierreMixto);

    public void RegenerarSecreto(string secretoFirmaCifrado)
    {
        ValidarSecreto(secretoFirmaCifrado);
        SecretoFirma = secretoFirmaCifrado;
    }

    private static void ValidarSecreto(string secretoFirmaCifrado)
    {
        if (string.IsNullOrWhiteSpace(secretoFirmaCifrado))
            throw new DomainException("La suscripción necesita un secreto de firma cifrado.");
    }
}

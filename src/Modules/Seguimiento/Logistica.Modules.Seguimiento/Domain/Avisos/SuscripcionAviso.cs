using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Domain.Avisos;

internal sealed class SuscripcionAviso : Entity, IOperadorOwned, IComercioOwned
{
    private readonly List<TipoEventoAviso> _tiposEvento = [];

    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public Guid RelacionComercialId { get; private set; }
    public string UrlDestino { get; private set; } = "";
    public IReadOnlyCollection<TipoEventoAviso> TiposEvento => _tiposEvento.AsReadOnly();
    // La aplicación entrega el valor ya cifrado; el dominio no conoce el proveedor de cifrado.
    public string SecretoFirma { get; private set; } = "";
    public bool Activa { get; private set; }

    private SuscripcionAviso() { }

    public static SuscripcionAviso Crear(Guid operadorId, Guid comercioId, Guid relacionComercialId,
        string urlDestino, IReadOnlyCollection<TipoEventoAviso> tiposEvento, string secretoFirmaCifrado)
    {
        if (operadorId == Guid.Empty || comercioId == Guid.Empty || relacionComercialId == Guid.Empty)
            throw new DomainException("La suscripción necesita operador, comercio y relación comercial válidos.");

        var direccion = urlDestino?.Trim() ?? "";
        if (!Uri.TryCreate(direccion, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host))
            throw new DomainException("La dirección del aviso debe ser una URL HTTPS válida.");

        if (tiposEvento is null || tiposEvento.Count == 0 || tiposEvento.Any(t => !Enum.IsDefined(t)))
            throw new DomainException("La suscripción necesita al menos un tipo de evento válido.");

        ValidarSecreto(secretoFirmaCifrado);

        var suscripcion = new SuscripcionAviso
        {
            OperadorId = operadorId,
            ComercioId = comercioId,
            RelacionComercialId = relacionComercialId,
            UrlDestino = direccion,
            SecretoFirma = secretoFirmaCifrado,
            Activa = true
        };
        suscripcion._tiposEvento.AddRange(tiposEvento.Distinct());
        return suscripcion;
    }

    public void Desactivar() => Activa = false;

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

using System.ComponentModel.DataAnnotations;
using Logistica.Http.Contracts.Avisos;

namespace Logistica.PortalComercio.Pages.Avisos;

public sealed class NuevaSuscripcionFormulario : IValidatableObject
{
    [Required(ErrorMessage = "Ingresá la dirección HTTPS de tu sistema.")]
    public string UrlDestino { get; set; } = "";
    public bool CambioDeEstado { get; set; }
    public bool EntregaFallida { get; set; }
    public bool Devolucion { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(UrlDestino) &&
            (!Uri.TryCreate(UrlDestino.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
             string.IsNullOrWhiteSpace(uri.Host)))
            yield return new("Ingresá una URL HTTPS válida.", [nameof(UrlDestino)]);
        if (!CambioDeEstado && !EntregaFallida && !Devolucion)
            yield return new("Elegí al menos un tipo de evento.");
    }

    public CrearSuscripcionAvisoRequest ARequest()
    {
        List<string> tipos = [];
        if (CambioDeEstado) tipos.Add("CambioDeEstado");
        if (EntregaFallida) tipos.Add("EntregaFallida");
        if (Devolucion) tipos.Add("Devolucion");
        return new(UrlDestino.Trim(), tipos);
    }
}

using System.ComponentModel.DataAnnotations;
using Logistica.Http.Contracts.Envios;

namespace Logistica.PortalComercio.Pages.Envios;

// Lo que el comercio completa en la pantalla. Es mutable porque EditForm escribe en sus propiedades;
// al confirmar se convierte en el CrearEnvioRequest que espera la API.
// Estas validaciones son sólo para avisar rápido: las reglas reales las vuelve a aplicar el servidor.
public sealed class NuevoEnvioFormulario
{
    [Required(ErrorMessage = "Ingresá el nombre del destinatario.")]
    public string Nombre { get; set; } = "";

    [Required(ErrorMessage = "Ingresá el teléfono del destinatario.")]
    public string Telefono { get; set; } = "";

    public string? Email { get; set; }

    public string? Documento { get; set; }

    [Required(ErrorMessage = "Ingresá la calle.")]
    public string Calle { get; set; } = "";

    [Required(ErrorMessage = "Ingresá el número de puerta.")]
    public string NumeroPuerta { get; set; } = "";

    [Required(ErrorMessage = "Ingresá la localidad.")]
    public string Localidad { get; set; } = "";

    [Required(ErrorMessage = "Elegí el departamento.")]
    public string Departamento { get; set; } = "Montevideo";

    [Required(ErrorMessage = "Ingresá el código postal.")]
    public string CodigoPostal { get; set; } = "";

    public string? Referencia { get; set; }

    public List<BultoFormulario> Bultos { get; } = [new()];

    public CrearEnvioRequest ARequest() => new(
        new DestinatarioRequest(Nombre.Trim(), Telefono.Trim(), Opcional(Email), Opcional(Documento)),
        new DireccionRequest(
            Calle.Trim(),
            NumeroPuerta.Trim(),
            Localidad.Trim(),
            Departamento,
            CodigoPostal.Trim(),
            Opcional(Referencia)),
        Bultos.Select(b => new BultoRequest(b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm)).ToList());

    // Un campo opcional que quedó vacío viaja como null, no como "".
    private static string? Opcional(string? valor)
    {
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }
}

public sealed class BultoFormulario
{
    public decimal PesoKg { get; set; }
    public decimal LargoCm { get; set; }
    public decimal AnchoCm { get; set; }
    public decimal AltoCm { get; set; }
}

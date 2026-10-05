using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

// Dónde se entrega. Sin Id propio, igual que el destinatario.
internal sealed record Direccion
{
    public string Calle { get; private init; }
    public string Numero { get; private init; }
    public string Localidad { get; private init; }
    public string Departamento { get; private init; }
    public string CodigoPostal { get; private init; }
    public string? Referencia { get; private init; }

    public Direccion(string calle, string numero, string localidad, string departamento,
        string codigoPostal, string? referencia = null)
    {
        Calle = Requerido(calle, "la calle");
        Numero = Requerido(numero, "el número de puerta");
        Localidad = Requerido(localidad, "la localidad");
        Departamento = Requerido(departamento, "el departamento");
        CodigoPostal = Requerido(codigoPostal, "el código postal");
        Referencia = referencia;
    }

    private static string Requerido(string valor, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new DomainException($"Falta {campo} de la dirección.");
        }

        return valor.Trim();
    }
}

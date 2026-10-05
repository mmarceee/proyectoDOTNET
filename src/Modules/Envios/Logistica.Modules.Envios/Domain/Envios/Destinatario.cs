using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

// Quien recibe el envío. Sin Id propio: es un dato del envío, no un registro compartido entre envíos.
internal sealed record Destinatario
{
    public string Nombre { get; private init; }
    public string Telefono { get; private init; }
    public string? Email { get; private init; }
    public string? Documento { get; private init; }

    public Destinatario(string nombre, string telefono, string? email = null, string? documento = null)
    {
        Nombre = Requerido(nombre, "el nombre");
        Telefono = Requerido(telefono, "el teléfono");
        Email = email;
        Documento = documento;
    }

    private static string Requerido(string valor, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new DomainException($"Falta {campo} del destinatario.");
        }

        return valor.Trim();
    }
}

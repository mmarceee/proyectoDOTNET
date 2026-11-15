using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;

namespace Logistica.Modules.Seguimiento.Application.Features.SolicitarReprogramacion;

// CU-61: el secreto con que el destinatario confirma la reprogramación. Del secreto se guarda un HMAC con la clave del
// token (Seguimiento:ClaveToken) y el envío: aunque alguien leyera la base, no podría probar los códigos sin la clave.
internal sealed class SecretoVerificacion(ClaveSeguimiento clave)
{
    // Un código de 6 dígitos al azar, con el generador criptográfico (no Random, que es predecible).
    public static string NuevoCodigo() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    // Los últimos 4 dígitos del teléfono, sin espacios ni guiones. null si tiene menos de 4.
    public static string? UltimosDigitos(string telefono)
    {
        var digitos = Digitos(telefono);
        return digitos.Length >= 4 ? digitos[^4..] : null;
    }

    // a****@gmail.com: alcanza para que el destinatario reconozca su correo sin mostrárselo a quien tenga el enlace.
    public static string Enmascarar(string email)
    {
        var arroba = email.IndexOf('@');
        if (arroba <= 0) return "****";
        return email[0] + new string('*', Math.Max(3, arroba - 1)) + email[arroba..];
    }

    // Se comparan sólo los dígitos: "0 1 2 3" y "0123" son el mismo código.
    public byte[] Hash(Guid envioId, string? secreto)
    {
        byte[] datos = [.. envioId.ToByteArray(), .. Encoding.UTF8.GetBytes(Digitos(secreto))];
        return HMACSHA256.HashData(clave.Valor, datos);
    }

    private static string Digitos(string? texto) => new((texto ?? "").Where(char.IsAsciiDigit).ToArray());
}

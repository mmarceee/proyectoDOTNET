using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Logistica.Modules.Seguimiento.Application.Abstractions;

namespace Logistica.Modules.Seguimiento.Infrastructure.Security;

internal static class FirmadorAvisos
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static byte[] Serializar(MensajeAviso mensaje) => JsonSerializer.SerializeToUtf8Bytes(mensaje, Json);

    public static string Fecha(DateTimeOffset fecha) => fecha.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    public static string Firmar(byte[] cuerpo, string fecha, string secretoBase64)
    {
        var clave = Convert.FromBase64String(secretoBase64);
        if (clave.Length != 32) throw new ArgumentException("La clave de firma debe contener 32 bytes.", nameof(secretoBase64));
        try
        {
            var entrada = Encoding.UTF8.GetBytes(fecha + ".");
            byte[] datos = [.. entrada, .. cuerpo];
            return "v1=" + Convert.ToHexStringLower(HMACSHA256.HashData(clave, datos));
        }
        finally { CryptographicOperations.ZeroMemory(clave); }
    }
}

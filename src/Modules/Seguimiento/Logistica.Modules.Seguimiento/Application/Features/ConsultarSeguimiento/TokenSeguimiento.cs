using System.Buffers.Text;
using System.Security.Cryptography;

namespace Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;

// El token del enlace público de seguimiento (CU-60; ADR-0002, sección 2.5). Lleva el envío y el operador, más una firma
// HMAC-SHA256 con una clave secreta de la configuración (Seguimiento:ClaveToken):
// - no es un número que se pueda adivinar ni recorrer (letra 6.3);
// - si se cambia un solo carácter, la firma no coincide y el token no sirve (A1);
// - el inquilino sale del token, ya verificado, antes de tocar cualquier dato.
// El mismo envío da siempre el mismo token, así que no hace falta guardarlo.
internal sealed class TokenSeguimiento(ClaveSeguimiento clave)
{
    private const int LargoFirma = 16;

    public string Generar(Guid operadorId, Guid envioId)
    {
        var datos = Datos(envioId, operadorId);
        return Base64Url.EncodeToString([.. datos, .. Firma(datos)]);
    }

    // false si el token no tiene la forma esperada o la firma no coincide.
    public bool TryLeer(string? token, out Guid operadorId, out Guid envioId)
    {
        operadorId = Guid.Empty;
        envioId = Guid.Empty;

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(token ?? "");
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length != 32 + LargoFirma)
        {
            return false;
        }

        // Comparación de tiempo constante: el tiempo de respuesta no da pistas sobre cuántos bytes coinciden.
        var datos = bytes[..32];
        if (!CryptographicOperations.FixedTimeEquals(bytes[32..], Firma(datos)))
        {
            return false;
        }

        envioId = new Guid(datos[..16]);
        operadorId = new Guid(datos[16..]);
        return true;
    }

    private static byte[] Datos(Guid envioId, Guid operadorId) => [.. envioId.ToByteArray(), .. operadorId.ToByteArray()];

    private byte[] Firma(byte[] datos) => HMACSHA256.HashData(clave.Valor, datos)[..LargoFirma];
}

// La clave secreta para firmar los tokens: al menos 32 bytes, en Base64. Sin clave, el seguimiento no funciona y lo
// dice claramente, en lugar de firmar con una clave débil.
internal sealed class ClaveSeguimiento(string? base64)
{
    public byte[] Valor { get; } = Leer(base64);

    private static byte[] Leer(string? base64)
    {
        var valor = string.IsNullOrWhiteSpace(base64) ? [] : Convert.FromBase64String(base64);
        return valor.Length >= 32
            ? valor
            : throw new InvalidOperationException(
                "Falta Seguimiento:ClaveToken (al menos 32 bytes en Base64) para firmar los enlaces de seguimiento.");
    }
}

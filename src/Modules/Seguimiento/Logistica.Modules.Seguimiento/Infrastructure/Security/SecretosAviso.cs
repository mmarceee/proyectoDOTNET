using System.Security.Cryptography;
using System.Text;
using Logistica.Modules.Seguimiento.Application.Abstractions;

namespace Logistica.Modules.Seguimiento.Infrastructure.Security;

// Clave maestra externa a la base, común a la API y al Worker. Cada cifrado tiene su propio nonce.
internal sealed class SecretosAviso(string? claveBase64) : ISecretosAviso
{
    private const int LargoSecreto = 32;
    private const int LargoNonce = 12;
    private const int LargoTag = 16;
    private const string Prefijo = "v1.";
    private readonly Lazy<byte[]> _clave = new(() => LeerClave(claveBase64));

    public string Generar() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(LargoSecreto));

    public string Cifrar(string secreto, Guid operadorId, Guid comercioId)
    {
        var datos = Convert.FromBase64String(secreto);
        if (datos.Length != LargoSecreto)
            throw new ArgumentException("El secreto del aviso debe contener 32 bytes en Base64.", nameof(secreto));
        var nonce = RandomNumberGenerator.GetBytes(LargoNonce);
        var cifrado = new byte[datos.Length];
        var tag = new byte[LargoTag];
        try
        {
            using var aes = new AesGcm(_clave.Value, LargoTag);
            aes.Encrypt(nonce, datos, cifrado, tag, Contexto(operadorId, comercioId));
            return Prefijo + Convert.ToBase64String([.. nonce, .. cifrado, .. tag]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(datos);
        }
    }

    public string Descifrar(string secretoCifrado, Guid operadorId, Guid comercioId)
    {
        if (!secretoCifrado.StartsWith(Prefijo, StringComparison.Ordinal))
            throw new CryptographicException("El formato del secreto cifrado no es válido.");
        var sobre = Convert.FromBase64String(secretoCifrado[Prefijo.Length..]);
        if (sobre.Length != LargoNonce + LargoSecreto + LargoTag)
            throw new CryptographicException("El formato del secreto cifrado no es válido.");
        var datos = new byte[LargoSecreto];
        try
        {
            using var aes = new AesGcm(_clave.Value, LargoTag);
            aes.Decrypt(sobre.AsSpan(0, LargoNonce), sobre.AsSpan(LargoNonce, LargoSecreto),
                sobre.AsSpan(LargoNonce + LargoSecreto, LargoTag), datos, Contexto(operadorId, comercioId));
            return Convert.ToBase64String(datos);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(datos);
        }
    }

    private static byte[] Contexto(Guid operadorId, Guid comercioId)
    {
        if (operadorId == Guid.Empty || comercioId == Guid.Empty)
            throw new ArgumentException("El cifrado de avisos requiere operador y comercio.");
        return Encoding.UTF8.GetBytes($"Logistica.Avisos.v1:{operadorId:N}:{comercioId:N}");
    }

    private static byte[] LeerClave(string? base64)
    {
        byte[] valor;
        try { valor = string.IsNullOrWhiteSpace(base64) ? [] : Convert.FromBase64String(base64); }
        catch (FormatException) { throw ErrorConfiguracion(); }
        return valor.Length == 32 ? valor : throw ErrorConfiguracion();
    }

    private static InvalidOperationException ErrorConfiguracion() => new(
        "Configurá Seguimiento:Avisos:ClaveCifrado con exactamente 32 bytes en Base64, iguales en API y Worker.");
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Domain.Avisos;

namespace Logistica.Modules.Seguimiento.Infrastructure.Security;

internal static class FirmadorAvisos
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // RF 36: cada versión tiene su forma fija. La v1 conserva los mismos campos, en el mismo orden, que antes de la
    // versión: un receptor existente recibe exactamente los mismos bytes. La firma es la misma en las dos versiones.
    public static byte[] Serializar(MensajeAviso mensaje) => mensaje.Version >= SuscripcionAviso.VersionConBultos
        ? JsonSerializer.SerializeToUtf8Bytes(new ContenidoV2(mensaje.Id, mensaje.Version, mensaje.Tipo, mensaje.FechaEvento,
            mensaje.NumeroEnvio, mensaje.Estado,
            mensaje.Bulto is { } bulto ? new BultoV2(bulto.Codigo, bulto.Estado, bulto.Motivo) : null, mensaje.Prueba), Json)
        : JsonSerializer.SerializeToUtf8Bytes(new ContenidoV1(mensaje.Id, mensaje.Tipo, mensaje.FechaEvento,
            mensaje.NumeroEnvio, mensaje.Estado, mensaje.Prueba), Json);

    private sealed record ContenidoV1(Guid Id, string Tipo, DateTimeOffset FechaEvento, string NumeroEnvio, string Estado,
        bool Prueba);

    private sealed record ContenidoV2(Guid Id, int Version, string Tipo, DateTimeOffset FechaEvento, string NumeroEnvio,
        string Estado, BultoV2? Bulto, bool Prueba);

    private sealed record BultoV2(string Codigo, string Estado, string? Motivo);

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

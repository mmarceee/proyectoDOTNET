using System.Net;
using System.Net.Sockets;

namespace Logistica.Modules.Seguimiento.Infrastructure.Http;

internal static class ConectorAvisosHttp
{
    public static SocketsHttpHandler CrearHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        MaxResponseHeadersLength = 16,
        PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        ConnectCallback = ConectarAsync
    };

    private static async ValueTask<Stream> ConectarAsync(SocketsHttpConnectionContext contexto, CancellationToken ct)
    {
        var direcciones = await Dns.GetHostAddressesAsync(contexto.DnsEndPoint.Host, ct);
        ValidarDirecciones(direcciones);
        SocketException? ultimoError = null;
        foreach (var direccion in direcciones)
        {
            var socket = new Socket(direccion.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                // Se conecta a la IP ya validada, sin una segunda resolución DNS. TLS conserva el host original.
                await socket.ConnectAsync(new IPEndPoint(direccion, contexto.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex) { socket.Dispose(); ultimoError = ex; }
            catch { socket.Dispose(); throw; }
        }
        throw new HttpRequestException("No se pudo conectar con el receptor.", ultimoError);
    }

    internal static void ValidarDirecciones(IPAddress[] direcciones)
    {
        if (direcciones.Length == 0 || direcciones.Any(ip => !EsPublica(ip)))
            throw new HttpRequestException("El receptor debe resolver exclusivamente a direcciones públicas.");
    }

    internal static bool EsPublica(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
            return !(b[0] is 0 or 10 or 127 || b[0] >= 224 ||
                b[0] == 100 && b[1] is >= 64 and <= 127 ||
                b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] is >= 16 and <= 31 ||
                b[0] == 192 && (b[1] == 168 || b[1] == 0 && b[2] is 0 or 2 || b[1] == 88 && b[2] == 99) ||
                b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100) ||
                b[0] == 203 && b[1] == 0 && b[2] == 113);
        // Sólo unicast global, excluyendo rangos especiales, documentación y túneles 6to4.
        return ip.AddressFamily == AddressFamily.InterNetworkV6 && (b[0] & 0xe0) == 0x20 &&
            !(b[0] == 0x20 && (b[1] == 2 || b[1] == 1 &&
                ((b[2] & 0xfe) == 0 || b[2] == 0x0d && b[3] == 0xb8))) &&
            !(b[0] == 0x3f && b[1] == 0xff && (b[2] & 0xf0) == 0);
    }
}

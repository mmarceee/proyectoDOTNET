using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Application.HechosEnvio;

// Un hecho de un envío, leído de un evento de Envíos. Lo usan los avisos al comercio (CU-64) y las notificaciones al
// destinatario (CU-62). EsAlta: es EnvioAdmitido, el único con el comercio en el sobre y los datos del destinatario.
internal sealed record HechoEnvio(Guid EventoId, Guid EnvioId, string? Numero, string EstadoEnvio, bool IntentoFallido,
    DateTimeOffset FechaEvento, bool EsAlta, string? NombreDestinatario = null, string? EmailDestinatario = null,
    DateOnly? NuevaFecha = null);

// El alta del envío todavía no llegó: el orden entre colas no está garantizado (ADR-0003, sección 2.4), así que
// el mensaje se reintenta con la espera creciente hasta que llegue.
internal sealed class EnvioNoRegistradoException(Guid envioId)
    : Exception($"Seguimiento todavía no registró el envío {envioId}.");

// La copia del envío en Seguimiento: la crea el alta y la leen los hechos posteriores.
internal sealed class EnviosSeguidos(IEnviosSeguidosRepository envios, ICurrentTenant tenant)
{
    public async Task<EnvioSeguido> ObtenerOCrearAsync(HechoEnvio hecho, CancellationToken ct)
    {
        if (await envios.ObtenerAsync(hecho.EnvioId, ct) is { } existente) return existente;
        if (!hecho.EsAlta) throw new EnvioNoRegistradoException(hecho.EnvioId);

        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("El mensaje no tiene operador.");
        // El comercio sale del sobre, nunca del contenido del evento (ADR-0003, sección 2.2).
        var comercioId = tenant.ComercioId
            ?? throw new InvalidDataException("El alta del envío no trae el comercio en el sobre.");
        var envio = EnvioSeguido.Crear(operadorId, comercioId, hecho.EnvioId,
            hecho.Numero ?? throw new InvalidDataException("El alta del envío no trae el número."),
            hecho.NombreDestinatario, hecho.EmailDestinatario);
        envios.Agregar(envio);
        return envio;
    }
}

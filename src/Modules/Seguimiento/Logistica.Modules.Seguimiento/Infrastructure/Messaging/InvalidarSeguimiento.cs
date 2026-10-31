using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Infrastructure.Messaging;

// CU-60, requerimiento 3 (ADR-0006, sección 2.3): ante cada cambio de estado de un envío, borra su seguimiento de la
// caché para que la destinataria vea el estado nuevo. Corre en el Worker, con el operador del mensaje como inquilino,
// así la clave es la misma que guardó la API. Borrar dos veces no hace daño, así que no necesita inbox; si falla, el
// dato vence solo en un minuto.
internal sealed class InvalidarSeguimiento(ICacheDistribuida cache) : IConsumidorIntegracion
{
    // Los eventos de Envíos que cambian lo que se muestra; cada uno con su cola.
    public static readonly string[] Tipos =
    [
        EnvioAsignadoARuta.Tipo, EnvioDesasignadoDeRuta.Tipo, EnvioEnTransito.Tipo, EnvioEntregado.Tipo,
        IntentoFallidoRegistrado.Tipo, EnvioReprogramado.Tipo, EnvioCancelado.Tipo, EnvioReintegradoADeposito.Tipo,
        EnvioRecibidoEnDeposito.Tipo, EnvioDevuelto.Tipo,
        EnvioExtraviado.Tipo, DevolucionIniciada.Tipo,
    ];

    public static string Cola(string tipo) => $"seguimiento.invalidar.{tipo}";

    public async Task ProcesarAsync(MensajeIntegracion mensaje, CancellationToken ct)
    {
        using var contenido = JsonDocument.Parse(mensaje.Contenido);
        if (!contenido.RootElement.TryGetProperty("EnvioId", out var propiedad) || !propiedad.TryGetGuid(out var envioId))
        {
            throw new InvalidDataException("El evento no tiene el Id del envío.");
        }

        await cache.InvalidarAsync(ConsultarSeguimientoHandler.Cache, ConsultarSeguimientoHandler.Clave(envioId), ct);
    }
}

using System.Text;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace Logistica.BuildingBlocks.Infrastructure.Messaging.Fallidos;

// CU-09 sobre la tabla mensajes_fallidos. El DbContext tiene el inquilino de la sesión: cada administrador ve y
// resuelve sólo los mensajes de su operador.
public sealed class MensajesFallidos(MensajeriaDbContext db, IConfiguration configuration, TimeProvider reloj) : IMensajesFallidos
{
    private const int Maximo = 200;

    public async Task<IReadOnlyList<MensajeFallidoResumen>> ListarAsync(bool incluirResueltos, CancellationToken ct)
    {
        var consulta = db.MensajesFallidos.AsNoTracking();
        if (!incluirResueltos) consulta = consulta.Where(m => m.Estado == EstadoMensajeFallido.Pendiente);
        var filas = await consulta.OrderByDescending(m => m.ActualizadoEn).Take(Maximo).ToListAsync(ct);
        return filas.Select(Resumen).ToList();
    }

    public async Task<MensajeFallidoDetalle?> ObtenerAsync(Guid id, CancellationToken ct) =>
        await db.MensajesFallidos.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct) is { } m
            ? new(Resumen(m), m.MessageId, m.Contenido, m.Historial, m.ResueltoPor, m.ResueltoEn, m.MotivoDescarte)
            : null;

    public async Task<int> ReintentarAsync(IReadOnlyCollection<Guid> ids, string usuario, CancellationToken ct)
    {
        var filas = await db.MensajesFallidos
            .Where(m => ids.Contains(m.Id) && m.Estado == EstadoMensajeFallido.Pendiente).ToListAsync(ct);
        if (filas.Count == 0) return 0;

        await using var conexion = await RabbitMqTransport.CrearFactory(configuration).CreateConnectionAsync(ct);
        // Con confirmaciones del broker y mandatory: si la cola de origen ya no existe, la publicación falla.
        await using var canal = await conexion.CreateChannelAsync(new CreateChannelOptions(true, true), ct);
        try
        {
            foreach (var fila in filas)
            {
                // Mismo MessageId y sin la cabecera de intentos: recorre de nuevo los reintentos (A3), y la inbox del
                // consumidor evita repetir un efecto que ya se aplicó (requerimiento 2).
                await canal.BasicPublishAsync("", fila.ColaOrigen, mandatory: true,
                    RabbitMqTransport.Propiedades(fila.Sobre()), Encoding.UTF8.GetBytes(fila.Contenido), ct);
                fila.MarcarReintentado(usuario, reloj.GetUtcNow());
            }
        }
        finally
        {
            // Los que llegaron a publicarse quedan Reintentados aunque un mensaje posterior falle.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return filas.Count;
    }

    public async Task<bool> DescartarAsync(Guid id, string motivo, string usuario, CancellationToken ct)
    {
        if (await db.MensajesFallidos.SingleOrDefaultAsync(m => m.Id == id, ct) is not { } fila) return false;
        fila.Descartar(usuario, motivo, reloj.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static MensajeFallidoResumen Resumen(MensajeFallido m) =>
        new(m.Id, m.Tipo, m.ColaOrigen, m.OcurridoEn, m.ActualizadoEn, m.Intentos, m.UltimoError, m.Estado.ToString());
}

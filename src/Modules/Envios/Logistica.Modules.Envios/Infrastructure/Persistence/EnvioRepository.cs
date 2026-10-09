using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

// Las consultas pasan por el filtro global "Tenant" (ADR-0002): sólo ven los envíos de la sesión.
internal sealed class EnvioRepository(EnviosDbContext db) : IEnvioRepository
{
    // SQL fijo, sin datos del usuario. "Value" es el nombre de columna que EF espera en SqlQueryRaw.
    private const string SiguienteNumeroSql = "SELECT nextval('envios.numero_envio') AS \"Value\"";

    public async Task<string> SiguienteNumeroAsync(CancellationToken ct)
    {
        var valor = await db.Database.SqlQueryRaw<long>(SiguienteNumeroSql).SingleAsync(ct);
        return $"ENV-{valor:D6}";
    }

    public Task<Envio?> ObtenerPorCodigoBultoAsync(string codigoBulto, CancellationToken ct)
    {
        return db.Envios
            .Include(e => e.Bultos)
            .SingleOrDefaultAsync(e => e.Bultos.Any(b => b.Codigo == codigoBulto), ct);
    }

    public Task<Envio?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        return db.Envios.SingleOrDefaultAsync(e => e.Id == id, ct);
    }

    public void Agregar(Envio envio)
    {
        db.Envios.Add(envio);
    }

    public Task GuardarCambiosAsync(CancellationToken ct)
    {
        return db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Envio>> ObtenerParaPlanificacionAsync(IReadOnlyCollection<Guid>? envioIds, CancellationToken ct)
    {
        var consulta = db.Envios.Include(e => e.Bultos).AsQueryable();
        consulta = envioIds is null ? consulta.Where(e => e.Estado == EstadoEnvio.EnDeposito || e.Estado == EstadoEnvio.Reprogramado)
            : consulta.Where(e => envioIds.Contains(e.Id));
        return await consulta.OrderBy(e => e.Numero).ToListAsync(ct);
    }
    public void RegistrarAsignacion(Guid operadorId, Guid envioId, Guid rutaId, Guid? responsableId, DateTimeOffset ahora)
    {
        var messageId = Guid.CreateVersion7();
        db.OutboxMessages.Add(Logistica.BuildingBlocks.Infrastructure.Persistence.OutboxMessage.Crear(operadorId,
            "EnvioAsignadoARuta.v1", new { MessageId = messageId, OperadorId = operadorId, EnvioId = envioId,
                RutaId = rutaId, ResponsableId = responsableId, OcurridoEn = ahora, Origen = "Backoffice", Version = 1 }, ahora, messageId));
    }

}

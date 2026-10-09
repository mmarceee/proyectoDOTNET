using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

internal sealed class EnvioRepository(EnviosDbContext db, ICurrentTenant tenant) : IEnvioRepository
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
        return DelInquilino()
            .Include(e => e.Bultos)
            .SingleOrDefaultAsync(e => e.Bultos.Any(b => b.Codigo == codigoBulto), ct);
    }

    public Task<Envio?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        return DelInquilino().SingleOrDefaultAsync(e => e.Id == id, ct);
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
        var consulta = DelInquilino().Include(e => e.Bultos).AsQueryable();
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

    // Filtro de inquilino manual hasta que exista el filtro global "Tenant" (ADR-0002, 15/10).
    // Falla cerrado: sin operador no devuelve nada.
    private IQueryable<Envio> DelInquilino()
    {
        if (tenant.OperadorId is not Guid operadorId)
        {
            return db.Envios.Where(_ => false);
        }

        var envios = db.Envios.Where(e => e.OperadorId == operadorId);

        if (tenant.ComercioId is Guid comercioId)
        {
            envios = envios.Where(e => e.ComercioId == comercioId);
        }

        return envios;
    }
}

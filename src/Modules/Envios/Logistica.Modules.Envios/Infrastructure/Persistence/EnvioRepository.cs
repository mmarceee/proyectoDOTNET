using Logistica.Modules.Envios.Contracts.Events;
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

    // El operador y el comercio del sobre los pone AgregarAlOutbox desde la sesión.
    public void RegistrarAsignacion(Guid envioId, Guid rutaId, Guid? responsableId, DateTimeOffset ahora)
    {
        db.AgregarAlOutbox(EnvioAsignadoARuta.Tipo,
            new EnvioAsignadoARuta(envioId, rutaId, responsableId, nameof(OrigenEvento.Backoffice)), ahora);
    }
}

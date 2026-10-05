using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

internal sealed class EnvioRepository(EnviosDbContext db) : IEnvioRepository
{
    // SQL fijo, sin datos del usuario. "Value" es el nombre de columna que EF espera en SqlQueryRaw.
    private const string SiguienteNumeroSql = "SELECT nextval('envios.numero_envio') AS \"Value\"";

    public async Task<string> SiguienteNumeroAsync(CancellationToken ct)
    {
        var valor = await db.Database.SqlQueryRaw<long>(SiguienteNumeroSql).SingleAsync(ct);
        return $"ENV-{valor:D6}";
    }

    public void Agregar(Envio envio)
    {
        db.Envios.Add(envio);
    }

    public Task GuardarCambiosAsync(CancellationToken ct)
    {
        return db.SaveChangesAsync(ct);
    }
}

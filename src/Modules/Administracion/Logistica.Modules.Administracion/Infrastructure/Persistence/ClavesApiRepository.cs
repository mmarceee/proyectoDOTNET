using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Administracion.Application.Features.GestionarClavesApi;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Comercios;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

internal sealed class ClavesApiRepository(AdministracionDbContext db, ICurrentTenant tenant, IUnidadDeTrabajo unidad)
    : IClavesApiRepository
{
    public async Task<IReadOnlyList<ClaveApi>> ListarAsync(CancellationToken ct) =>
        await db.ClavesApi.AsNoTracking().OrderByDescending(c => c.CreadaEn).ThenByDescending(c => c.Id).ToListAsync(ct);

    public Task<ClaveApi?> ObtenerAsync(Guid id, CancellationToken ct) =>
        db.ClavesApi.SingleOrDefaultAsync(c => c.Id == id, ct);

    public void Agregar(ClaveApi clave) => db.ClavesApi.Add(clave);
    public Task GuardarAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public Task<IdentidadClaveApi?> IdentificarPorHashAsync(string hash, CancellationToken ct) =>
        // ADR-0002 §2.4: la búsqueda exacta por hash es anterior a resolver el tenant.
        db.ClavesApi.IgnoreQueryFilters([ModuleDbContext.FiltroTenant]).AsNoTracking()
            .Where(clave => clave.Hash == hash && clave.RevocadaEn == null)
            .Select(clave => new IdentidadClaveApi(clave.OperadorId, clave.ComercioId, clave.Tipo == TipoClaveApi.Prueba))
            .SingleOrDefaultAsync(ct);

    public async Task<bool> RegistrarUsoPorHashAsync(string hash, DateTimeOffset ahora, CancellationToken ct)
    {
        var vigente = false;
        await unidad.EjecutarAsync(async () =>
        {
            await db.ParticiparEnTransaccionAsync(ct);
            // El bloqueo serializa el registro de último uso con la revocación. EF compone el filtro Tenant.
            var clave = await db.ClavesApi.FromSql($"""
                SELECT * FROM administracion."ClavesApi"
                WHERE "Hash" = {hash} AND "OperadorId" = {tenant.OperadorId} AND "ComercioId" = {tenant.ComercioId}
                FOR UPDATE
                """).SingleOrDefaultAsync(ct);

            if (clave is null || clave.RevocadaEn is not null) return;

            // Con el tenant fijado, se valida la relación antes de admitir la operación.
            if (!await db.RelacionesComerciales.AnyAsync(r =>
                r.Id == clave.RelacionComercialId && r.Estado == EstadoRelacion.Activa, ct))
                return;

            clave.RegistrarUso(ahora);
            await db.SaveChangesAsync(ct);
            vigente = true;
        }, ct);
        // El alta usa este contexto para cotizar. Liberar la transacción antes de esas lecturas.
        await db.ParticiparEnTransaccionAsync(ct);
        return vigente;
    }
}

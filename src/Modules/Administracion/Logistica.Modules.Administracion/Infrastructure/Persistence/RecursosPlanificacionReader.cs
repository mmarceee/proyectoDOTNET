using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain;
using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.Modules.Administracion.Application.Features.ConsultarRecursosPlanificacion;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

internal sealed class RecursosPlanificacionReader(AdministracionDbContext db, ICurrentTenant tenant) : IRecursosPlanificacionReader
{
    private Guid Operador => tenant.OperadorId ?? throw new DomainException("La sesión no tiene un operador.");
    public async Task BloquearPlanificacionAsync(CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("La reserva requiere una unidad de trabajo activa.");
        // Operador es global. SQL parametrizado, con el ID exclusivamente de la sesión (ADR-0002 §4).
        var filas = await db.Operadores.FromSqlInterpolated($"SELECT * FROM administracion.\"Operadores\" WHERE \"Id\" = {Operador} FOR UPDATE")
            .AsNoTracking().ToListAsync(ct);
        if (filas.Count != 1 || !filas[0].Activo) throw new DomainException("El operador no está disponible.");
    }
    public async Task<ContextoPlanificacion> ObtenerContextoPlanificacionAsync(CancellationToken ct)
    {
        var operador = await db.Operadores.AsNoTracking().SingleOrDefaultAsync(o => o.Id == Operador && o.Activo, ct)
            ?? throw new DomainException("El operador no está disponible.");
        return new(operador.Id, operador.ZonaHoraria);
    }
    // El máximo de paradas sale de las reglas operativas de CU-06 que rigen al confirmar la operación (acordado con Lucas,
    // 19/10). Se devuelve el Id de esa versión: el historial de la ruta conserva la versión y el límite que usó, y
    // publicar otra versión no lo cambia, porque una versión publicada no se modifica.
    public async Task<ReglasPlanificacion> ObtenerReglasPlanificacionAsync(DateTimeOffset ahora, CancellationToken ct)
    {
        var regla = await db.VersionesReglas.AsNoTracking()
            .Where(r => r.Estado != EstadoVersion.Borrador && r.VigenteDesde <= ahora
                && (r.VigenteHasta == null || ahora < r.VigenteHasta))
            .OrderByDescending(r => r.Numero).Select(r => new { r.Id, r.MaxParadasPorRuta }).FirstOrDefaultAsync(ct)
            ?? throw new DomainException("El operador no tiene reglas operativas vigentes.");
        return new(regla.Id, regla.MaxParadasPorRuta);
    }
    public async Task<DatosRecursosPlanificacion> LeerRecursosAsync(CancellationToken ct)
    {
        var repartidores = await db.Repartidores.AsNoTracking().Where(r => r.Activo).OrderBy(r => r.Nombre).ToListAsync(ct);
        var vehiculos = await db.Vehiculos.AsNoTracking().Where(v => v.Activo).OrderBy(v => v.Matricula).ToListAsync(ct);
        var zonas = await db.Zonas.AsNoTracking().Where(z => z.Activa).ToListAsync(ct);
        var franjas = await db.Franjas.AsNoTracking().ToListAsync(ct);
        return new(repartidores, vehiculos, zonas, franjas);
    }
}

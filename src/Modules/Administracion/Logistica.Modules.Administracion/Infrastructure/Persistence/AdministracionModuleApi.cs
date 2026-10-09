using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

internal sealed class AdministracionModuleApi(AdministracionDbContext db, ICurrentTenant tenant) : IAdministracionModuleApi
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
    public async Task<RecursosPlanificacion> ConsultarRecursosPlanificacionAsync(DateOnly fecha, CancellationToken ct)
    {
        // Se retira la guarda explícita al integrar el filtro global de Ezequiel; no se crea otro interceptor.
        var operador = Operador;
        var repartidores = await db.Repartidores.AsNoTracking().Where(r => r.OperadorId == operador && r.Activo).OrderBy(r => r.Nombre).ToListAsync(ct);
        var vehiculos = await db.Vehiculos.AsNoTracking().Where(v => v.OperadorId == operador && v.Activo).OrderBy(v => v.Matricula).ToListAsync(ct);
        var zonas = await db.Zonas.AsNoTracking().Where(z => z.OperadorId == operador && z.Activa).ToListAsync(ct);
        var franjas = await db.Franjas.AsNoTracking().Where(f => f.OperadorId == operador).ToListAsync(ct);
        return new(repartidores.Select(r => new RepartidorPlanificacion(r.Id, r.Nombre, r.Activo)).ToList(),
            vehiculos.Select(v => new VehiculoPlanificacion(v.Id, v.Matricula, v.Activo, v.CapacidadPesoKg,
                v.CapacidadVolumenM3, v.LargoCargaCm, v.AnchoCargaCm, v.AltoCargaCm)).ToList(),
            zonas.Select(z => new ZonaPlanificacion(z.Id, z.Nombre, z.CodigosPostales)).ToList(),
            franjas.Where(f => f.Aplica(fecha) && zonas.Any(z => z.Id == f.ZonaId)).Select(f => MapearFranja(f.Id, f)).ToList());
    }
    public async Task<ReglasPlanificacion> ObtenerReglasPlanificacionAsync(DateTimeOffset ahora, CancellationToken ct)
    {
        var operador = Operador;
        var regla = await db.ReglasPlanificacion.AsNoTracking().Where(r => r.OperadorId == operador
            && r.VigenteDesde <= ahora && (r.VigenteHasta == null || ahora < r.VigenteHasta)).OrderByDescending(r => r.VigenteDesde).FirstOrDefaultAsync(ct)
            ?? throw new DomainException("El operador no tiene reglas de planificación vigentes.");
        return new(regla.Id, regla.MaxParadasPorRuta);
    }
    public async Task<IReadOnlyList<FranjaPlanificacion>> ResolverFranjasAsync(IReadOnlyCollection<Guid> ids, DateOnly fecha, CancellationToken ct)
    {
        var operador = Operador;
        var zonasActivas = await db.Zonas.AsNoTracking().Where(z => z.OperadorId == operador && z.Activa).Select(z => z.Id).ToListAsync(ct);
        var franjas = await db.Franjas.AsNoTracking().Where(f => f.OperadorId == operador).ToListAsync(ct);
        var resultado = new List<FranjaPlanificacion>();
        foreach (var id in ids.Distinct())
        {
            var franja = franjas.SingleOrDefault(f => f.Id == id);
            var visitadas = new HashSet<Guid>();
            while (franja is not null && visitadas.Add(franja.Id))
            {
                if (franja.Aplica(fecha) && zonasActivas.Contains(franja.ZonaId)) { resultado.Add(MapearFranja(id, franja)); break; }
                if (fecha < franja.VigenteDesde) break;
                franja = franjas.SingleOrDefault(f => f.ReemplazaAId == franja.Id);
            }
        }
        return resultado;
    }
    private static FranjaPlanificacion MapearFranja(Guid referencia, Domain.Planificacion.FranjaHoraria f)
        => new(referencia, f.Id, f.ZonaId, f.HoraDesde, f.HoraHasta, f.Dias.Select(d => (DayOfWeek)d).ToArray(), f.VigenteDesde, f.VigenteHasta);
}

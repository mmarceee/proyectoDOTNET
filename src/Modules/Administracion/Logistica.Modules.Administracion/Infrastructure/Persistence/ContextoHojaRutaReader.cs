using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

// La relación usuario-repartidor se vuelve a comprobar incluso con una cookie emitida anteriormente.
internal sealed class ContextoHojaRutaReader(AdministracionDbContext db, ICurrentTenant tenant) : IContextoHojaRuta
{
    public async Task<ContextoHojaRuta?> ConsultarAsync(Guid usuarioId, Guid repartidorId, Guid? vehiculoId, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        if (!await db.Repartidores.AnyAsync(r => r.Id == repartidorId && r.UsuarioId == usuarioId && r.Activo, ct)) return null;
        var operador = await db.Operadores.AsNoTracking().SingleOrDefaultAsync(o => o.Id == tenant.OperadorId && o.Activo, ct);
        if (operador is null) return null;
        var matricula = vehiculoId is null ? null
            : await db.Vehiculos.Where(v => v.Id == vehiculoId).Select(v => v.Matricula).SingleOrDefaultAsync(ct);
        var zonas = await db.Zonas.AsNoTracking().ToListAsync(ct);
        var franjas = await db.Franjas.AsNoTracking().ToListAsync(ct);
        return new(operador.Id, operador.ZonaHoraria, matricula,
            zonas.Select(z => new ZonaHojaRuta(z.Id, z.Codigo, z.Nombre, z.CodigosPostales)).ToArray(),
            franjas.Select(f => new FranjaHojaRuta(f.Id, f.ReemplazaAId, f.ZonaId, f.HoraDesde, f.HoraHasta,
                f.VigenteDesde, f.VigenteHasta, f.Dias.Select(d => (DayOfWeek)d).ToArray())).ToArray());
    }
}

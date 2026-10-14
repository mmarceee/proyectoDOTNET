using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Administracion.Application.Features.SeleccionarOperador;
using Logistica.Modules.Administracion.Domain.Comercios;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Administracion.Infrastructure.Persistence;

internal sealed class OperadoresDelComercio(AdministracionDbContext db) : IOperadoresDelComercio
{
    public async Task<IReadOnlyList<OperadorDisponible>> ActivosAsync(Guid comercioId, CancellationToken ct)
    {
        // ADR-0002, sección 2.4, tercer caso: antes de elegir operador, la sesión del comercio no tiene inquilino y el
        // filtro no le muestra ninguna relación. Única consulta autorizada: ignora sólo el filtro "Tenant", filtra por
        // el ComercioId de la cookie (lo pasa el llamador desde la sesión, nunca desde el cliente) y devuelve sólo
        // la identificación de cada operador. Operador no tiene filtro: es la tabla de los inquilinos.
        // Se ordena por la columna antes de armar el resultado: EF no puede traducir a SQL un orden sobre el objeto ya creado.
        return await (
                from r in db.RelacionesComerciales.IgnoreQueryFilters([ModuleDbContext.FiltroTenant]).AsNoTracking()
                join o in db.Operadores.AsNoTracking() on r.OperadorId equals o.Id
                where r.ComercioId == comercioId && r.Estado == EstadoRelacion.Activa && o.Activo
                orderby o.Nombre
                select new OperadorDisponible(o.Id, o.Nombre))
            .ToListAsync(ct);
    }
}

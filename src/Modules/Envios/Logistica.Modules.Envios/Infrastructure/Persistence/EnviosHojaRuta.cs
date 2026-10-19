using Logistica.Modules.Envios.Application;
using Logistica.Modules.Envios.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

internal sealed class EnviosHojaRuta(EnviosDbContext db, IReglasDeEntrega reglas) : IEnviosHojaRuta
{
    public async Task<IReadOnlyList<EnvioParaHoja>> ConsultarAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        await db.ParticiparEnTransaccionAsync(ct);
        var envios = await db.Envios.AsNoTracking().Include(e => e.Bultos).Where(e => ids.Contains(e.Id)).ToListAsync(ct);
        var resultado = new List<EnvioParaHoja>();
        foreach (var e in envios)
        {
            var d = e.Direccion;
            resultado.Add(new(e.Id, e.Numero, e.Estado.ToString(), e.Destinatario.Nombre, e.Destinatario.Telefono,
                d.Calle, d.Numero, d.Localidad, d.Departamento, d.CodigoPostal, d.Referencia, e.ZonaId, e.FranjaHorariaId,
                e.Bultos.OrderBy(b => b.Codigo).Select(b => new BultoParaHoja(b.Id, b.Codigo)).ToArray(),
                await reglas.ParaHojaAsync(e, ct)));
        }
        return resultado;
    }
}

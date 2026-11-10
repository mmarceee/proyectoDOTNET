using Logistica.Modules.Envios.Application.Features.ImprimirEtiquetas;
using Logistica.Modules.Envios.Domain.Envios;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

internal sealed class EtiquetasReader(EnviosDbContext db) : IEtiquetasReader
{
    public async Task<IReadOnlyList<EnvioParaEtiquetas>> ConsultarAsync(string[] numeros, CancellationToken ct) =>
        await db.Envios.AsNoTracking().Where(e => numeros.Contains(e.Numero))
            .Select(e => new EnvioParaEtiquetas(e.Numero,
                e.Estado == EstadoEnvio.Cancelado || e.Bultos.All(b => b.Estado == EstadoEnvio.Cancelado),
                e.Destinatario.Nombre,
                e.Direccion.Calle + " " + e.Direccion.Numero + "\n" + e.Direccion.Localidad + ", " +
                    e.Direccion.Departamento + " · CP " + e.Direccion.CodigoPostal +
                    (e.Direccion.Referencia == null ? "" : "\n" + e.Direccion.Referencia),
                e.ZonaId, e.Modalidad == Logistica.Modules.Administracion.Contracts.Modalidad.Urgente ? "Urgente" : "Estándar",
                e.Bultos.Count,
                e.Bultos.OrderBy(b => b.Codigo).Take(ImprimirEtiquetasHandler.MaximoEtiquetas + 1).Select(b => b.Codigo).ToList()))
            .ToListAsync(ct);
}

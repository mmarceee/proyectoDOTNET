using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Envios.Application.Features.ConsultarEnviosApi;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

internal sealed class EnviosApiReader(EnviosDbContext db, IConsultaConsistente lectura) : IEnviosApiReader
{
    public Task<PaginaEnviosApiResponse> ListarAsync(FiltrosEnviosApi filtros, bool esPrueba, CancellationToken ct) =>
        lectura.LeerAsync(async () =>
        {
            await db.ParticiparEnTransaccionAsync(ct);
            var consulta = db.Envios.DelAmbiente(esPrueba).AsNoTracking();
            if (filtros.Estado is { } estado)
            {
                var estados = Enum.GetValues<EstadoEnvio>().Where(e => ProyeccionEnvioApi.ResumenDe(e) == estado).ToArray();
                if (estados.Length == 0)
                    return new PaginaEnviosApiResponse(filtros.Pagina, filtros.TamanoPagina, 0, []);
                consulta = consulta.Where(e => estados.Contains(e.Estado));
            }
            if (filtros.FechaDesde is { } desde)
            {
                var inicio = FechasConsultaEnvios.InicioDelDiaUtc(desde);
                consulta = consulta.Where(e => e.CreadoEn >= inicio);
            }
            if (filtros.FechaHasta is { } hasta && hasta < DateOnly.MaxValue)
            {
                var fin = FechasConsultaEnvios.InicioDelDiaUtc(hasta.AddDays(1));
                consulta = consulta.Where(e => e.CreadoEn < fin);
            }
            if (filtros.ReferenciaComercio is { } referencia)
                consulta = consulta.Where(e => e.ReferenciaComercio == referencia);

            var total = await consulta.CountAsync(ct);
            var salto = (long)(filtros.Pagina - 1) * filtros.TamanoPagina;
            if (salto >= total)
                return new PaginaEnviosApiResponse(filtros.Pagina, filtros.TamanoPagina, total, []);

            var filas = await consulta.OrderByDescending(e => e.CreadoEn).ThenByDescending(e => e.Numero)
                .Skip((int)salto).Take(filtros.TamanoPagina)
                .Select(e => new { e.Numero, e.ReferenciaComercio, e.CreadoEn, e.MontoTarifa, e.Modalidad, e.Estado, Total = e.Bultos.Count })
                .ToListAsync(ct);
            var envios = filas.Select(e => ProyeccionEnvioApi.Resumen(e.Numero, e.ReferenciaComercio, e.CreadoEn,
                e.MontoTarifa, e.Modalidad, e.Estado, e.Total)).ToArray();

            return new PaginaEnviosApiResponse(filtros.Pagina, filtros.TamanoPagina, total, envios);
        }, ct);

    public Task<DetalleEnvioApiResponse?> ObtenerAsync(string numero, bool esPrueba, CancellationToken ct) =>
        lectura.LeerAsync(async () =>
        {
            await db.ParticiparEnTransaccionAsync(ct);
            // La transacción de lectura mantiene una misma vista para el resumen, los bultos y el historial.
            var envio = await db.Envios.DelAmbiente(esPrueba).AsNoTracking().AsSplitQuery()
                .Include(e => e.Bultos).Include(e => e.Eventos)
                .SingleOrDefaultAsync(e => e.Numero == numero, ct);
            if (envio is null) return null;

            var resumen = ProyeccionEnvioApi.Resumen(envio.Numero, envio.ReferenciaComercio, envio.CreadoEn,
                envio.MontoTarifa, envio.Modalidad, envio.Estado, envio.Bultos.Count);
            var resultadoBulto = ProyeccionEnvioApi.ResultadoBulto(envio.Estado);
            var bultos = envio.Bultos.OrderBy(b => b.Codigo).Select(b => new BultoEnvioApiResponse(b.Codigo, resultadoBulto)).ToArray();
            var historial = envio.Eventos.OrderBy(e => e.OcurridoEn).ThenBy(e => e.Id).Select(ProyeccionEnvioApi.Evento).ToArray();
            return new DetalleEnvioApiResponse(resumen, bultos, historial);
        }, ct);
}

using Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Modules.Envios.Infrastructure.Persistence;

internal sealed class EnvioDetalleReader(
    EnviosDbContext db,
    ICurrentTenant tenant) : IEnvioDetalleReader
{
    public async Task<EnvioDetalleDto?> ConsultarAsync(
        ConsultarDetalleEnvioQuery query,
        CancellationToken ct)
    {
        if (tenant.OperadorId is not Guid operadorId)
        {
            return null;
        }

        // Aislamiento manual hasta que estén disponibles los filtros globales (ADR-0002).
        var consulta = db.Envios
            .AsNoTracking()
            .Where(e => e.OperadorId == operadorId && e.Numero == query.Numero);

        if (tenant.ComercioId is Guid comercioId)
        {
            consulta = consulta.Where(e => e.ComercioId == comercioId);
        }

        // Consultas separadas para evitar multiplicar las filas de bultos por las de eventos.
        var encontrado = await consulta
            .AsSplitQuery()
            .Select(e => new { e.Id, Detalle = new EnvioDetalleDto(
                e.Numero,
                e.ComercioId,
                e.Estado.ToString(),
                e.CreadoEn,
                e.MontoTarifa,
                new DestinatarioDetalleDto(
                    e.Destinatario.Nombre,
                    e.Destinatario.Telefono,
                    e.Destinatario.Email,
                    e.Destinatario.Documento),
                new DireccionDetalleDto(
                    e.Direccion.Calle,
                    e.Direccion.Numero,
                    e.Direccion.Localidad,
                    e.Direccion.Departamento,
                    e.Direccion.CodigoPostal,
                    e.Direccion.Referencia),
                e.Bultos.OrderBy(b => b.Codigo)
                    .Select(b => new BultoDetalleDto(
                        b.Codigo,
                        b.PesoKg,
                        b.LargoCm,
                        b.AnchoCm,
                        b.AltoCm,
                        b.MontoTarifa))
                    .ToList(),
                e.Eventos.OrderBy(ev => ev.OcurridoEn).ThenBy(ev => ev.Id)
                    .Select(ev => new EventoEnvioDetalleDto(
                        ev.EstadoAnterior.HasValue ? ev.EstadoAnterior.Value.ToString() : null,
                        ev.EstadoNuevo.ToString(),
                        ev.OcurridoEn,
                        ev.Origen.ToString(),
                        ev.ResponsableId,
                        ev.Latitud,
                        ev.Longitud,
                        ev.Detalle))
                    .ToList(),
                e.VersionTarifarioId.HasValue && e.VersionTarifarioNumero.HasValue
                    ? new VersionTarifarioDetalleDto(e.VersionTarifarioId.Value, e.VersionTarifarioNumero.Value) : null,
                e.Intentos.OrderBy(i => i.NumeroIntento)
                    .Select(i => new IntentoEntregaDetalleDto(
                        i.NumeroIntento, i.FechaHora, i.Resultado.ToString(), i.MotivoNoEntregaId, i.Observaciones,
                        i.Evidencia == null ? null : new PruebaEntregaDetalleDto(
                            i.Evidencia.FirmaArchivoId, i.Evidencia.FotoArchivoId, i.Evidencia.NombreReceptor,
                            i.Evidencia.DocumentoReceptor, i.Evidencia.Latitud, i.Evidencia.Longitud, i.Evidencia.CapturadaEn)))
                    .ToList(),
                new List<IncidenciaDetalleDto>(),
                null) })
            .SingleOrDefaultAsync(ct);

        if (encontrado is null)
        {
            return null;
        }

        var incidencias = db.Incidencias.AsNoTracking()
            .Where(i => i.EnvioId == encontrado.Id && i.OperadorId == operadorId);
        var devoluciones = db.Devoluciones.AsNoTracking()
            .Where(d => d.EnvioId == encontrado.Id && d.OperadorId == operadorId);
        if (tenant.ComercioId is Guid comercio)
        {
            incidencias = incidencias.Where(i => i.ComercioId == comercio);
            devoluciones = devoluciones.Where(d => d.ComercioId == comercio);
        }

        return encontrado.Detalle with
        {
            Incidencias = await incidencias.OrderBy(i => i.CreadaEn).ThenBy(i => i.Id)
                .Select(i => new IncidenciaDetalleDto(i.Tipo.ToString(), i.Descripcion, i.Estado.ToString(),
                    i.CreadaEn, i.ResueltaEn, i.Resolucion)).ToListAsync(ct),
            Devolucion = await devoluciones.Select(d => new DevolucionDetalleDto(d.Motivo, d.Estado.ToString(),
                d.IniciadaEn, d.RecibidaEnDepositoEn, d.NombreReceptor, d.DocumentoReceptor,
                d.EntregadaAlComercioEn)).SingleOrDefaultAsync(ct),
        };
    }
}

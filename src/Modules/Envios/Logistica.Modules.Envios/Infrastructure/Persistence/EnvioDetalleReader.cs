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
        return await consulta
            .AsSplitQuery()
            .Select(e => new EnvioDetalleDto(
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
                        ev.ResponsableId))
                    .ToList()))
            .SingleOrDefaultAsync(ct);
    }
}

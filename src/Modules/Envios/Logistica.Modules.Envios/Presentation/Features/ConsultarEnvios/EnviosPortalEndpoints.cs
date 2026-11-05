using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;
using Logistica.Modules.Envios.Application.Features.ConsultarEnvios;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Envios.Presentation.Features.ConsultarEnvios;

internal static class EnviosPortalEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var portal = group.MapGroup("/portal").RequireAuthorization(Politicas.Comercio);
        portal.MapGet("", ListarAsync);
        portal.MapGet("/{numero}", DetalleAsync);
    }

    private static async Task<IResult> ListarAsync(int? pagina, DateOnly? fechaDesde, DateOnly? fechaHasta,
        string? texto, ResumenEnvio? resumen, Guid? zonaId, Modalidad? modalidad,
        ConsultarEnviosHandler handler, IAdministracionModuleApi administracion, ICurrentTenant tenant, CancellationToken ct)
    {
        if (tenant.OperadorId is null || tenant.ComercioId is null)
            return Results.Forbid();

        ConsultarEnviosResult resultado;
        try
        {
            resultado = await handler.HandleAsync(new(Pagina: pagina ?? 1, FechaDesde: fechaDesde,
                FechaHasta: fechaHasta, Texto: texto, Resumen: resumen, ZonaId: zonaId, Modalidad: modalidad), ct);
        }
        catch (ArgumentException error)
        {
            return Results.BadRequest(new { mensaje = error.Message });
        }

        var zonas = (await administracion.ConsultarZonasAsync(ct)).Select(z => new ZonaEnviosPortal(z.Id, z.Nombre)).ToList();
        var nombres = zonas.ToDictionary(z => z.Id, z => z.Nombre);
        var filas = resultado.Envios.Select(e => new EnvioPortalFila(e.Numero, e.ReferenciaComercio,
            e.DestinatarioNombre, e.Localidad, e.ZonaId is Guid id ? nombres.GetValueOrDefault(id) : null,
            EstadoEnvioTexto.Modalidad(e.Modalidad), EstadoEnvioTexto.Resumen(e.Resumen, e.Terminado, e.BultosEntregados),
            e.Terminado, e.BultosEntregados, e.TotalBultos, e.CreadoEn, e.MontoTarifa)).ToList();

        return Results.Ok(new PaginaEnviosPortal(filas, resultado.Pagina, resultado.TotalPaginas, resultado.TotalRegistros, zonas));
    }

    private static async Task<IResult> DetalleAsync(string numero, ConsultarDetalleEnvioHandler handler,
        ICurrentTenant tenant, CancellationToken ct)
    {
        if (tenant.OperadorId is null || tenant.ComercioId is null)
            return Results.Forbid();

        if (string.IsNullOrWhiteSpace(numero) || await handler.HandleAsync(new(numero), ct) is not { } detalle)
            return Results.NotFound();

        return Results.Ok(new DetalleEnvioPortal(detalle.Numero,
            EstadoEnvioTexto.Resumen(detalle.Resumen, detalle.Terminado, detalle.BultosEntregados),
            detalle.Terminado, detalle.BultosEntregados,
            detalle.Bultos.Select(b => new BultoEnvioPortal(b.Codigo, EstadoEnvioTexto.Mostrar(b.Estado))).ToList()));
    }
}

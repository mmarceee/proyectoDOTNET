using System.Globalization;
using System.Text;
using Logistica.Http.Contracts.Deposito;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Deposito.Application.Features.ConsultarCuentaCorriente;
using Logistica.Modules.Deposito.Domain.Liquidaciones;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Logistica.Modules.Deposito.Presentation.Features.ConsultarCuentaCorriente;

// CU-33. El Portal pide la cuenta corriente y el detalle; la descarga (paso 5) es un enlace común, así que la cookie de
// la sesión viaja sola y no hace falta JavaScript.
internal static class ConsultarCuentaCorrienteEndpoint
{
    // Punto y coma y coma decimal: así lo abre bien una planilla configurada en español.
    private static readonly NumberFormatInfo Numeros = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "" };

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var grupo = endpoints.MapGroup("/api/cuenta-corriente").WithTags("Deposito")
            .RequireAuthorization(Politicas.Comercio);

        grupo.MapGet("", async (ConsultarCuentaCorrienteHandler handler, CancellationToken ct) =>
        {
            var cuenta = await handler.ConsultarAsync(ct);
            return TypedResults.Ok(new CuentaCorrienteResponse(cuenta.SaldoPendiente,
                cuenta.Liquidaciones.Select(Resumen).ToList()));
        });
        grupo.MapGet("/liquidaciones/{id:guid}", ObtenerAsync);
        grupo.MapGet("/liquidaciones/{id:guid}/archivo", DescargarAsync);
    }

    private static async Task<Results<Ok<LiquidacionDetalleResponse>, NotFound>> ObtenerAsync(Guid id,
        ConsultarCuentaCorrienteHandler handler, CancellationToken ct)
    {
        if (await handler.ObtenerAsync(id, ct) is not { } detalle)
            return TypedResults.NotFound();
        return TypedResults.Ok(new LiquidacionDetalleResponse(Resumen(detalle.Liquidacion),
            detalle.Lineas.Select(l => new LineaLiquidacionResponse(l.NumeroEnvio, l.Fecha, l.Concepto, l.Monto)).ToList()));
    }

    private static async Task<Results<FileContentHttpResult, NotFound>> DescargarAsync(Guid id,
        ConsultarCuentaCorrienteHandler handler, CancellationToken ct)
    {
        if (await handler.ObtenerAsync(id, ct) is not { } detalle)
            return TypedResults.NotFound();
        var nombre = $"liquidacion-{detalle.Liquidacion.PeriodoDesde:yyyy-MM-dd}-al-{detalle.Liquidacion.PeriodoHasta:yyyy-MM-dd}.csv";
        return TypedResults.File(Csv(detalle), "text/csv; charset=utf-8", nombre);
    }

    // Con BOM, para que la planilla reconozca el UTF-8 de los acentos.
    internal static byte[] Csv(DetalleLiquidacion detalle)
    {
        var liquidacion = detalle.Liquidacion;
        var texto = new StringBuilder();
        texto.AppendLine($"Liquidación;del {Dia(liquidacion.PeriodoDesde)} al {Dia(liquidacion.PeriodoHasta)};{liquidacion.Estado}");
        texto.AppendLine("Envío;Fecha;Concepto;Monto");
        foreach (var linea in detalle.Lineas)
            texto.AppendLine($"{linea.NumeroEnvio};{Dia(linea.Fecha)};{linea.Concepto};{Monto(linea.Monto)}");
        texto.AppendLine($";;Total bruto;{Monto(liquidacion.TotalBruto)}");
        texto.AppendLine($";;Ajustes;{Monto(liquidacion.TotalAjustes)}");
        texto.AppendLine($";;Total neto;{Monto(liquidacion.TotalNeto)}");
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(texto.ToString())];
    }

    private static string Monto(decimal monto) => monto.ToString("0.00", Numeros);

    // Con la cultura invariante, la "/" del formato no depende del idioma del servidor.
    private static string Dia(DateOnly dia) => dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static LiquidacionResumenResponse Resumen(Liquidacion l) =>
        new(l.Id, l.PeriodoDesde, l.PeriodoHasta, l.Estado.ToString(), l.TotalBruto, l.TotalAjustes, l.TotalNeto,
            l.EmitidaEn, l.PagadaEn);
}

using Logistica.Modules.Envios.Application.Features.ConsultarEnvios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Logistica.Modules.Envios.Domain.Envios;
using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;

namespace Logistica.Modules.Envios.Presentation.Features.ConsultarEnvios;

internal sealed class IndexModel(
    ConsultarEnviosHandler handler, IAdministracionModuleApi administracion) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int TamanoPagina { get; set; } = 20;

    [BindProperty(SupportsGet = true)]
    public EstadoEnvio? Estado { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FechaDesde { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FechaHasta { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Texto { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? ComercioId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? ZonaId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Modalidad? Modalidad { get; set; }

    [BindProperty(SupportsGet = true)]
    public ResumenEnvio? Resumen { get; set; }

    public IReadOnlyList<ComercioRelacionado> Comercios { get; private set; } = [];
    public IReadOnlyList<ZonaPlanificacion> Zonas { get; private set; } = [];
    private IReadOnlyDictionary<Guid, string> NombresComercios { get; set; } = new Dictionary<Guid, string>();
    private IReadOnlyDictionary<Guid, string> NombresZonas { get; set; } = new Dictionary<Guid, string>();
    public string NombreComercio(Guid id) => NombresComercios.GetValueOrDefault(id) ?? "Sin identificar";
    public string NombreZona(Guid? id) => id is Guid zona ? NombresZonas.GetValueOrDefault(zona) ?? "Sin identificar" : "—";

    public ConsultarEnviosResult Resultado { get; private set; } =
        new([], 1, 20, 0);

    public Dictionary<string, string?> ParametrosListado =>
        new()
        {
            [nameof(Pagina)] = Resultado.Pagina == 1 ? null : Resultado.Pagina.ToString(CultureInfo.InvariantCulture),
            [nameof(TamanoPagina)] = Resultado.TamanoPagina == 20 ? null : Resultado.TamanoPagina.ToString(CultureInfo.InvariantCulture),
            [nameof(Estado)] = Estado?.ToString(),
            [nameof(FechaDesde)] = FechaDesde?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [nameof(FechaHasta)] = FechaHasta?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [nameof(Texto)] = string.IsNullOrWhiteSpace(Texto) ? null : Texto,
            [nameof(ComercioId)] = ComercioId?.ToString(),
            [nameof(ZonaId)] = ZonaId?.ToString(),
            [nameof(Modalidad)] = Modalidad?.ToString(),
            [nameof(Resumen)] = Resumen?.ToString(),
        };

    public string UrlPagina(int pagina)
    {
        var parametros = ParametrosListado;
        parametros[nameof(Pagina)] = pagina == 1 ? null : pagina.ToString(CultureInfo.InvariantCulture);
        return QueryHelpers.AddQueryString("/backoffice/envios", parametros);
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Comercios = await administracion.ConsultarComerciosAsync(ct);
        Zonas = await administracion.ConsultarZonasAsync(ct);
        NombresComercios = Comercios.ToDictionary(c => c.ComercioId, c => c.RazonSocial);
        NombresZonas = Zonas.ToDictionary(z => z.Id, z => z.Nombre);

        if (Pagina < 1 || TamanoPagina < 1 || TamanoPagina > 100)
        {
            ModelState.AddModelError(
                "",
                "Los parámetros de paginación son inválidos.");
        }

        if (Estado.HasValue && !Enum.IsDefined(Estado.Value))
        {
            ModelState.AddModelError(
                nameof(Estado),
                "El estado indicado no es válido.");
        }

        if (FechaDesde.HasValue &&
            FechaHasta.HasValue &&
            FechaDesde.Value > FechaHasta.Value)
        {
            ModelState.AddModelError(
                nameof(FechaHasta),
                "La fecha desde no puede ser posterior a la fecha hasta.");
        }

        if (Resumen is { } resumen && !Enum.IsDefined(resumen))
            ModelState.AddModelError(nameof(Resumen), "El resumen indicado no es válido.");
        if (Modalidad is { } modalidad && !Enum.IsDefined(modalidad))
            ModelState.AddModelError(nameof(Modalidad), "La modalidad indicada no es válida.");

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var query = new ConsultarEnviosQuery(
            Pagina,
            TamanoPagina,
            Estado,
            FechaDesde,
            FechaHasta,
            Texto,
            ComercioId,
            Resumen,
            ZonaId,
            Modalidad);

        Resultado = await handler.HandleAsync(query, ct);

        return Page();
    }
}

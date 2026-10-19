using Logistica.Modules.Administracion.Application.Features.ConfigurarTarifario;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Tarifas;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.ConfigurarTarifario;

// CU-05, pasos 5 a 9: editar las reglas y los ajustes del borrador y publicarlo. A1 y A3 se muestran como error; A2 se
// advierte y se pide confirmación. Sólo el perfil Administrador.
[Authorize(Policy = Politicas.Administrador)]
internal sealed class BorradorModel(
    ConsultarTarifarioHandler consultar, GestionarBorradorHandler gestionar, PublicarTarifarioHandler publicar) : PageModel
{
    public VersionDetalleDto Borrador { get; private set; } = null!;
    public IReadOnlyList<ZonaOpcionDto> Zonas { get; private set; } = [];
    public IReadOnlyList<ZonaSinTarifaDto> SinTarifa { get; private set; } = [];
    public string? Mensaje { get; private set; }

    // Una regla nueva. Los números llegan como texto (TextosTarifario.Decimal).
    [BindProperty] public Guid ZonaId { get; set; }
    [BindProperty] public Modalidad Modalidad { get; set; }
    [BindProperty] public string? PesoDesde { get; set; }
    [BindProperty] public string? PesoHasta { get; set; }
    [BindProperty] public string? VolumenDesde { get; set; }
    [BindProperty] public string? VolumenHasta { get; set; }
    [BindProperty] public string? Precio { get; set; }

    // Un ajuste nuevo.
    [BindProperty] public TipoAjuste TipoAjuste { get; set; }
    [BindProperty] public bool EsPorcentaje { get; set; } = true;
    [BindProperty] public string? ValorAjuste { get; set; }
    [BindProperty] public Modalidad ModalidadAjuste { get; set; }

    // Publicar: sin fecha es de inmediato.
    [BindProperty] public DateOnly? Fecha { get; set; }
    [BindProperty] public bool ConfirmaSinTarifa { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Mensaje = TempData["Mensaje"] as string;
        return await MostrarAsync(ct);
    }

    public Task<IActionResult> OnPostReglaAsync(CancellationToken ct)
    {
        return EjecutarAsync(async () => await gestionar.AgregarReglaAsync(new DatosReglaTarifa(ZonaId, Modalidad,
            TextosTarifario.Decimal(PesoDesde, "peso desde"), TextosTarifario.Decimal(PesoHasta, "peso hasta"),
            TextosTarifario.Decimal(VolumenDesde, "volumen desde"), TextosTarifario.Decimal(VolumenHasta, "volumen hasta"),
            TextosTarifario.Decimal(Precio, "precio")), ct) ? "Se agregó la regla." : null, ct);
    }

    public Task<IActionResult> OnPostQuitarReglaAsync(Guid id, CancellationToken ct)
    {
        return EjecutarAsync(async () => await gestionar.QuitarReglaAsync(id, ct) ? "Se quitó la regla." : null, ct);
    }

    public Task<IActionResult> OnPostAjusteAsync(CancellationToken ct)
    {
        return EjecutarAsync(async () => await gestionar.AgregarAjusteAsync(new DatosAjusteTarifa(TipoAjuste, EsPorcentaje,
            TextosTarifario.Decimal(ValorAjuste, "el valor del ajuste"), ModalidadAjuste), ct) ? "Se agregó el ajuste." : null, ct);
    }

    public Task<IActionResult> OnPostQuitarAjusteAsync(Guid id, CancellationToken ct)
    {
        return EjecutarAsync(async () => await gestionar.QuitarAjusteAsync(id, ct) ? "Se quitó el ajuste." : null, ct);
    }

    public async Task<IActionResult> OnPostPublicarAsync(CancellationToken ct)
    {
        try
        {
            var resultado = await publicar.PublicarAsync(Fecha, ConfirmaSinTarifa, ct);
            if (resultado.Publicada)
            {
                TempData["Mensaje"] = Fecha is null
                    ? "Se publicó la versión: rige desde ahora. Los envíos ya admitidos conservan su precio."
                    : $"Se publicó la versión: rige desde el {Fecha:dd/MM/yyyy}. Hasta entonces sigue la anterior.";
                return LocalRedirect("/backoffice/tarifario");
            }

            // A2: se muestra la advertencia y se pide confirmar.
            SinTarifa = resultado.SinTarifa;
        }
        catch (DomainException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
        }

        return await MostrarAsync(ct);
    }

    // Con éxito vuelve al borrador con el mensaje; null es "no hay borrador" (otro administrador lo publicó); una regla rota
    // se muestra en la página, sin perder lo escrito.
    private async Task<IActionResult> EjecutarAsync(Func<Task<string?>> accion, CancellationToken ct)
    {
        try
        {
            if (await accion() is not string mensaje)
            {
                TempData["Mensaje"] = "No hay un borrador en curso: puede que otro administrador lo haya publicado.";
                return LocalRedirect("/backoffice/tarifario");
            }

            TempData["Mensaje"] = mensaje;
            return LocalRedirect("/backoffice/tarifario/borrador");
        }
        catch (DomainException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
            return await MostrarAsync(ct);
        }
    }

    private async Task<IActionResult> MostrarAsync(CancellationToken ct)
    {
        if (await consultar.ObtenerBorradorAsync(ct) is not VersionDetalleDto borrador)
        {
            return LocalRedirect("/backoffice/tarifario");
        }

        Borrador = borrador;
        Zonas = await consultar.ZonasActivasAsync(ct);
        return Page();
    }
}

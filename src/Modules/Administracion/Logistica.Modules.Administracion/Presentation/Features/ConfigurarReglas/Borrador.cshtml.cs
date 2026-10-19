using Logistica.Modules.Administracion.Application.Features.ConfigurarReglas;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Reglas;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.ConfigurarReglas;

// CU-06, pasos 4 a 6: editar los valores y los motivos del borrador y publicarlo. Lo que impide publicar (A1 y A2) se
// muestra siempre, para corregirlo antes. Sólo el perfil Administrador.
[Authorize(Policy = Politicas.Administrador)]
internal sealed class BorradorModel(
    ConsultarReglasHandler consultar, GestionarBorradorReglasHandler gestionar, PublicarReglasHandler publicar) : PageModel
{
    public VersionReglasDetalleDto Borrador { get; private set; } = null!;
    public string? Mensaje { get; private set; }

    // Los valores: son enteros, así que no dependen del separador decimal.
    [BindProperty] public int MaxIntentos { get; set; }
    [BindProperty] public int PlazoEntreIntentosHoras { get; set; }
    [BindProperty] public bool ExigeFoto { get; set; }
    [BindProperty] public bool ExigeNombreYDocumento { get; set; }
    [BindProperty] public bool DevolverTrasIntentosAgotados { get; set; }
    [BindProperty] public int PlazoDevolucionDias { get; set; }
    [BindProperty] public int PlazoEstandarHoras { get; set; }
    [BindProperty] public int PlazoUrgenteHoras { get; set; }
    [BindProperty] public int AnticipacionReprogramacionHoras { get; set; }
    [BindProperty] public int MaxParadasPorRuta { get; set; }

    // Un motivo nuevo.
    [BindProperty] public string? Codigo { get; set; }
    [BindProperty] public string? Descripcion { get; set; }
    [BindProperty] public bool Reprogramable { get; set; } = true;
    [BindProperty] public bool ExigeEvidencia { get; set; }

    [BindProperty] public DateOnly? Fecha { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Mensaje = TempData["Mensaje"] as string;
        return await MostrarAsync(ct);
    }

    public Task<IActionResult> OnPostValoresAsync(CancellationToken ct)
    {
        var valores = new DatosReglas(MaxIntentos, PlazoEntreIntentosHoras, new PruebaExigida(ExigeFoto, ExigeNombreYDocumento),
            new PoliticaDevolucion(DevolverTrasIntentosAgotados, PlazoDevolucionDias),
            [new PlazoModalidad(Modalidad.Estandar, PlazoEstandarHoras), new PlazoModalidad(Modalidad.Urgente, PlazoUrgenteHoras)],
            AnticipacionReprogramacionHoras, MaxParadasPorRuta);
        return EjecutarAsync(async () => await gestionar.GuardarValoresAsync(valores, ct) ? "Se guardaron los valores." : null, ct);
    }

    public Task<IActionResult> OnPostMotivoAsync(CancellationToken ct)
    {
        return EjecutarAsync(async () => await gestionar.AgregarMotivoAsync(
            new DatosMotivo(Codigo ?? "", Descripcion ?? "", Reprogramable, ExigeEvidencia), ct) ? "Se agregó el motivo." : null, ct);
    }

    public Task<IActionResult> OnPostQuitarMotivoAsync(Guid id, CancellationToken ct)
    {
        return EjecutarAsync(async () => await gestionar.QuitarMotivoAsync(id, ct) ? "Se quitó el motivo." : null, ct);
    }

    public async Task<IActionResult> OnPostPublicarAsync(CancellationToken ct)
    {
        try
        {
            await publicar.PublicarAsync(Fecha, ct);
            TempData["Mensaje"] = Fecha is null
                ? "Se publicaron las reglas: rigen desde ahora para los envíos que se admitan. Los anteriores conservan las suyas."
                : $"Se publicaron las reglas: rigen desde el {Fecha:dd/MM/yyyy}. Hasta entonces siguen las anteriores.";
            return LocalRedirect("/backoffice/reglas");
        }
        catch (DomainException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
            return await MostrarAsync(ct);
        }
    }

    // Con éxito vuelve al borrador con el mensaje; null es "no hay borrador"; una regla rota se muestra en la página.
    private async Task<IActionResult> EjecutarAsync(Func<Task<string?>> accion, CancellationToken ct)
    {
        try
        {
            if (await accion() is not string mensaje)
            {
                TempData["Mensaje"] = "No hay un borrador en curso: puede que otro administrador lo haya publicado.";
                return LocalRedirect("/backoffice/reglas");
            }

            TempData["Mensaje"] = mensaje;
            return LocalRedirect("/backoffice/reglas/borrador");
        }
        catch (DomainException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
            return await MostrarAsync(ct);
        }
    }

    // Los campos muestran lo guardado; tras un error, los que se enviaron ganan (ModelState).
    private async Task<IActionResult> MostrarAsync(CancellationToken ct)
    {
        if (await consultar.ObtenerBorradorAsync(ct) is not VersionReglasDetalleDto borrador)
        {
            return LocalRedirect("/backoffice/reglas");
        }

        Borrador = borrador;
        var v = borrador.Valores;
        MaxIntentos = v.MaxIntentos;
        PlazoEntreIntentosHoras = v.PlazoEntreIntentosHoras;
        ExigeFoto = v.PruebaExigida.ExigeFoto;
        ExigeNombreYDocumento = v.PruebaExigida.ExigeNombreYDocumento;
        DevolverTrasIntentosAgotados = v.PoliticaDevolucion.DevolverTrasIntentosAgotados;
        PlazoDevolucionDias = v.PoliticaDevolucion.PlazoDevolucionDias;
        PlazoEstandarHoras = v.Plazos.FirstOrDefault(p => p.Modalidad == Modalidad.Estandar)?.PlazoHoras ?? 0;
        PlazoUrgenteHoras = v.Plazos.FirstOrDefault(p => p.Modalidad == Modalidad.Urgente)?.PlazoHoras ?? 0;
        AnticipacionReprogramacionHoras = v.AnticipacionReprogramacionHoras;
        MaxParadasPorRuta = v.MaxParadasPorRuta;
        return Page();
    }
}

using Logistica.Modules.Administracion.Application.Features.GestionarComercios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarComercios;

[Authorize(Policy = Politicas.Administrador)]
internal sealed class NuevoModel(GestionarComerciosHandler comercios) : PageModel
{
    [BindProperty] public string? DocumentoFiscal { get; set; } = "";
    [BindProperty] public string? RazonSocial { get; set; } = "";
    [BindProperty] public string? EmailContacto { get; set; } = "";
    public ComercioEncontrado? Existente { get; private set; }
    public bool Consultado { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostBuscarAsync(CancellationToken ct)
    {
        try
        {
            Existente = await comercios.BuscarAsync(DocumentoFiscal ?? "", ct);
            Consultado = true;
        }
        catch (DomainException e) { ModelState.AddModelError(nameof(DocumentoFiscal), e.Message); }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            Existente = await comercios.BuscarAsync(DocumentoFiscal ?? "", ct);
            Consultado = true;
            if (Existente?.Vinculado == true) return LocalRedirect($"/backoffice/comercios/{Existente.Id}");
            var id = await comercios.CrearAsync(DocumentoFiscal ?? "", RazonSocial ?? "", EmailContacto ?? "", ct);
            TempData["Mensaje"] = "El comercio quedó vinculado al operador.";
            return LocalRedirect($"/backoffice/comercios/{id}");
        }
        catch (DomainException e) { ModelState.AddModelError(string.Empty, e.Message); }
        return Page();
    }
}

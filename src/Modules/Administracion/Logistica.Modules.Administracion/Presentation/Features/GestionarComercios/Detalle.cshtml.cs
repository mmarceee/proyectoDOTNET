using Logistica.Modules.Administracion.Application.Features.GestionarComercios;
using Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Comercios;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarComercios;

[Authorize(Policy = Politicas.Administrador)]
internal sealed class DetalleModel(GestionarComerciosHandler comercios, IServicioUsuarios usuarios, ICurrentTenant tenant) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public uint Version { get; set; }
    [BindProperty] public string? EmailContacto { get; set; } = "";
    public ComercioVista Comercio { get; private set; } = null!;
    public bool TieneUsuario { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var comercio = await comercios.ObtenerAsync(Id, ct);
        if (comercio is null) return NotFound();
        Comercio = comercio;
        Version = comercio.Version;
        EmailContacto = comercio.EmailContacto;
        TieneUsuario = (await usuarios.ListarAsync(tenant.OperadorId!.Value, [Id], ct)).Any(u => u.ComercioId == Id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string accion, CancellationToken ct)
    {
        try
        {
            await comercios.ModificarAsync(Id, Version, accion, EmailContacto ?? "", ct);
            TempData["Mensaje"] = "La relación comercial se actualizó.";
            return LocalRedirect($"/backoffice/comercios/{Id}");
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (DomainException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
            Response.StatusCode = 400;
            // Preserva la versión enviada: un formulario obsoleto obliga a recargar explícitamente.
            var resultado = await OnGetAsync(ct);
            ModelState.Remove(nameof(EmailContacto));
            return resultado;
        }
    }
}

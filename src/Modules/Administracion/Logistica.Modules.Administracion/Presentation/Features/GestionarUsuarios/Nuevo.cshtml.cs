using System.ComponentModel.DataAnnotations;
using Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Hosting;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarUsuarios;

// CU-02, alta (pasos 3 a 6). Sólo el perfil Administrador.
[Authorize(Policy = Politicas.Administrador)]
internal sealed class NuevoModel(AltaUsuarioHandler alta, IDatosParaUsuarios datos, IHostEnvironment entorno) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Ingresá el nombre.")]
    public string Nombre { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Ingresá el correo.")]
    [EmailAddress(ErrorMessage = "El correo no es válido.")]
    public string Email { get; set; } = "";

    [BindProperty]
    public Perfil Perfil { get; set; } = Perfil.Despachador;

    // Paso 4: sólo para el usuario de comercio.
    [BindProperty]
    public Guid? ComercioId { get; set; }

    // Paso 4: opcional, sólo para el repartidor.
    [BindProperty]
    public Guid? RepartidorId { get; set; }

    public IReadOnlyList<ComercioDelOperador> Comercios { get; private set; } = [];
    public IReadOnlyList<RepartidorSinUsuario> Repartidores { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        await CargarOpcionesAsync(ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var invitacion = await alta.HandleAsync(new AltaUsuarioCommand(
                    Nombre, Email, Perfil,
                    Perfil == Perfil.UsuarioComercio ? ComercioId : null,
                    Perfil == Perfil.Repartidor ? RepartidorId : null), ct);

                TempData["Mensaje"] = "Se creó el usuario. Le llega un correo con el enlace para definir su contraseña.";

                // Mientras el Worker no mande el correo, en Development se muestra el enlace para poder probar.
                if (entorno.IsDevelopment())
                {
                    TempData["Enlace"] = invitacion.Enlace;
                }

                return LocalRedirect($"/backoffice/usuarios/{invitacion.UsuarioId}");
            }
            catch (DomainException e)
            {
                // A1 y A2: se indica el error y no se crea nada.
                ModelState.AddModelError(string.Empty, e.Message);
            }
        }

        await CargarOpcionesAsync(ct);
        return Page();
    }

    private async Task CargarOpcionesAsync(CancellationToken ct)
    {
        Comercios = (await datos.ComerciosAsync(ct)).Where(c => c.Activa).ToList();
        Repartidores = await datos.RepartidoresSinUsuarioAsync(ct);
    }
}

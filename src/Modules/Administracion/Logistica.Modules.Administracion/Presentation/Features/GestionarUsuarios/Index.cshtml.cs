using Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;
using Logistica.Modules.Administracion.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarUsuarios;

// CU-02, paso 2: el listado de usuarios del operador y de sus comercios. Sólo el perfil Administrador.
[Authorize(Policy = Politicas.Administrador)]
internal sealed class IndexModel(ConsultarUsuariosHandler consultar) : PageModel
{
    public IReadOnlyList<UsuarioVista> Usuarios { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Usuarios = await consultar.ListarAsync(ct);
    }
}

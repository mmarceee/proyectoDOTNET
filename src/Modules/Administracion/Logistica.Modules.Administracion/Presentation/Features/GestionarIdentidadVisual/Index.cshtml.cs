using Logistica.Modules.Administracion.Application.Features.GestionarIdentidadVisual;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Operadores;
using Logistica.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarIdentidadVisual;

// CU-07: el administrador configura logo, colores y contacto de su operador. Sólo el perfil Administrador.
[Authorize(Policy = Politicas.Administrador)]
internal sealed class IndexModel(
    ConsultarIdentidadVisualHandler consultar, ActualizarIdentidadVisualHandler actualizar) : PageModel
{
    [BindProperty]
    public string ColorPrimario { get; set; } = "";

    [BindProperty]
    public string ColorSecundario { get; set; } = "";

    [BindProperty]
    public string? EmailContacto { get; set; }

    [BindProperty]
    public string? TelefonoContacto { get; set; }

    // Opcional: sin archivo se conserva el logo actual.
    [BindProperty]
    public IFormFile? Logo { get; set; }

    public string NombreOperador { get; private set; } = "";
    public bool TieneLogo { get; private set; }
    public string? Mensaje { get; private set; }
    public string? Advertencia { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (await consultar.HandleAsync(ct) is not IdentidadVisualDto actual)
        {
            return NotFound();
        }

        ColorPrimario = actual.ColorPrimario;
        ColorSecundario = actual.ColorSecundario;
        EmailContacto = actual.EmailContacto;
        TelefonoContacto = actual.TelefonoContacto;
        Mostrar(actual);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            var resultado = await actualizar.HandleAsync(new ActualizarIdentidadVisualCommand(
                ColorPrimario, ColorSecundario, EmailContacto, TelefonoContacto, await LeerLogoAsync(ct)), ct);

            Mensaje = "Se guardó la identidad visual.";
            if (resultado.ContrasteInsuficiente)
            {
                // A3: se guarda igual, con el aviso.
                Advertencia = "El texto blanco puede no leerse bien sobre el color primario. Considerá un color más oscuro.";
            }
        }
        catch (DomainException e)
        {
            // A1 y A2: se indica el error y no se guarda nada.
            ModelState.AddModelError(string.Empty, e.Message);
        }

        if (await consultar.HandleAsync(ct) is IdentidadVisualDto actual)
        {
            Mostrar(actual);
        }

        return Page();
    }

    private void Mostrar(IdentidadVisualDto actual)
    {
        NombreOperador = actual.NombreOperador;
        TieneLogo = actual.LogoArchivoId is not null;
    }

    // Se lee como mucho un byte más que el máximo: alcanza para que el dominio rechace un archivo grande (A1)
    // sin cargarlo entero en memoria.
    private async Task<LogoSubido?> LeerLogoAsync(CancellationToken ct)
    {
        if (Logo is not { Length: > 0 })
        {
            return null;
        }

        await using var origen = Logo.OpenReadStream();
        var buffer = new byte[LogoOperador.MaximoBytes + 1];
        var leidos = 0;
        int n;
        while (leidos < buffer.Length && (n = await origen.ReadAsync(buffer.AsMemory(leidos), ct)) > 0)
        {
            leidos += n;
        }

        return new LogoSubido(Logo.ContentType, buffer[..leidos]);
    }
}

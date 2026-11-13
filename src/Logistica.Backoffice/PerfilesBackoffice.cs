using System.Security.Claims;

namespace Logistica.Backoffice;

// Cómo se muestra el perfil de la sesión en el Backoffice: un nombre, una clase de color y un ícono SVG propio, para
// distinguir de un vistazo a administración, despacho y depósito. Es sólo visual: la autorización la hace cada página.
// Area es cómo se nombra el espacio de trabajo en el saludo del Inicio. Color es el mismo de la clase en site.css: lo usa
// el ícono de la pestaña, que no hereda estilos de la página.
public sealed record PerfilBackoffice(string Rol, string Nombre, string Area, string Clase, string Color, string Icono);

public static class PerfilesBackoffice
{
    // Íconos de 24x24 de trazo, con el color del texto (currentColor). Son constantes del código, nunca datos del usuario.
    private const string Escudo =
        """<path d="M12 3l7 3v5c0 4.5-3 8.5-7 10c-4-1.5-7-5.5-7-10V6z"/><path d="M9 12l2 2l4-4"/>""";

    private const string Recorrido =
        """<circle cx="6" cy="18" r="2"/><circle cx="18" cy="6" r="2"/><path d="M8 18h7a3 3 0 0 0 0-6H9a3 3 0 0 1 0-6h7"/>""";

    private const string Caja =
        """<path d="M3 7.5l9-4.5l9 4.5v9l-9 4.5l-9-4.5z"/><path d="M3 7.5l9 4.5l9-4.5"/><path d="M12 12v9"/><path d="M7.5 5.25l9 4.5"/>""";

    private const string Globo =
        """<circle cx="12" cy="12" r="9"/><path d="M3 12h18"/><path d="M12 3c3 3.5 3 14.5 0 18c-3-3.5-3-14.5 0-18z"/>""";

    // En orden de prioridad, por si una cuenta tuviera más de un perfil.
    public static IReadOnlyList<PerfilBackoffice> Todos { get; } =
    [
        new("AdministradorGeneral", "Administrador general", "Administración general", "perfil-general", "#1e3a8a", Globo),
        new("Administrador", "Administración", "Administración", "perfil-administrador", "#5b21b6", Escudo),
        new("Despachador", "Despacho", "Despacho", "perfil-despacho", "#0f766e", Recorrido),
        new("OperarioDeposito", "Depósito", "Depósito", "perfil-deposito", "#b45309", Caja),
    ];

    public static PerfilBackoffice? DelUsuario(ClaimsPrincipal usuario) =>
        usuario.Identity?.IsAuthenticated == true ? Todos.FirstOrDefault(p => usuario.IsInRole(p.Rol)) : null;

    // El ícono de la pestaña del navegador: el mismo dibujo, en blanco sobre un cuadrado redondeado del color del perfil,
    // para reconocer la pestaña de cada perfil aunque haya varias abiertas. Va como data URI: no hace falta un archivo.
    public static string Favicon(PerfilBackoffice perfil)
    {
        var svg = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><rect width="24" height="24" rx="5" fill="{perfil.Color}"/><g transform="translate(3 3) scale(.75)" fill="none" stroke="#fff" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round">{perfil.Icono}</g></svg>""";
        return "data:image/svg+xml," + Uri.EscapeDataString(svg);
    }

    public static string Svg(PerfilBackoffice perfil, int tamano) =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" width="{tamano}" height="{tamano}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">{perfil.Icono}</svg>""";
}

using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Logistica.Backoffice;

public sealed record SeccionBackoffice(string Grupo, string Titulo, string Descripcion, string Ruta,
    IReadOnlyList<string> Perfiles);

// Las secciones del Backoffice, en el orden del menú. El menú lateral (_Layout) y las tarjetas de Inicio salen de esta
// lista, así que una página nueva se agrega acá una sola vez. Los perfiles son los mismos que exige cada página: el
// menú sólo oculta lo que el usuario no puede abrir; la autorización la sigue haciendo la página. Seguimiento (CU-60)
// no va: se entra desde cada fila de Envíos.
public static class SeccionesBackoffice
{
    private const string Administrador = "Administrador";
    private const string Despachador = "Despachador";
    private const string OperarioDeposito = "OperarioDeposito";

    public static IReadOnlyList<SeccionBackoffice> Todas { get; } =
    [
        new("Operación", "Envíos", "Buscá envíos por estado o fecha y mirá su historial.",
            "/backoffice/envios", [Administrador, Despachador, OperarioDeposito]),
        new("Operación", "Tablero", "El avance de la operación del día, en vivo.",
            "/backoffice/tablero", [Administrador, Despachador]),
        new("Operación", "Rutas", "Armá, ordená y despachá las rutas de reparto.",
            "/backoffice/rutas", [Despachador]),
        new("Operación", "Incidencias", "Reclamos, daños y extravíos de los envíos, hasta resolverlos.",
            "/backoffice/incidencias", [Administrador, Despachador]),
        new("Operación", "Reportes", "Cumplimiento, motivos de no entrega, volumen y liquidación por período.",
            "/backoffice/reportes", [Administrador, Despachador]),

        new("Depósito", "Recepción", "Registrá los bultos que llegan al depósito.",
            "/backoffice/deposito/recepcion", [OperarioDeposito]),
        new("Depósito", "Rendiciones", "Confirmá lo que traen los repartidores al cerrar la ruta.",
            "/backoffice/rendiciones", [OperarioDeposito]),
        new("Depósito", "Devoluciones", "Entregá al comercio los bultos devueltos.",
            "/backoffice/devoluciones", [OperarioDeposito]),

        new("Configuración", "Comercios", "Los comercios del operador y su relación comercial.",
            "/backoffice/comercios", [Administrador]),
        new("Configuración", "Usuarios", "Los usuarios del operador y de sus comercios, con sus perfiles.",
            "/backoffice/usuarios", [Administrador]),
        new("Configuración", "Zonas", "Las zonas de reparto y sus códigos postales.",
            "/backoffice/zonas", [Administrador]),
        new("Configuración", "Flota", "Los repartidores y los vehículos.",
            "/backoffice/flota", [Administrador]),
        new("Configuración", "Tarifario", "Las versiones del cuadro tarifario, con un simulador de precio.",
            "/backoffice/tarifario", [Administrador]),
        new("Configuración", "Reglas", "Las versiones de las reglas operativas: intentos, plazos y evidencia.",
            "/backoffice/reglas", [Administrador]),
        new("Configuración", "Identidad visual", "El logo, los colores y el contacto del operador.",
            "/backoffice/identidad-visual", [Administrador]),

        new("Administración", "Liquidaciones", "Generá, emití y cobrá las liquidaciones de cada comercio.",
            "/backoffice/liquidaciones", [Administrador]),
        new("Administración", "Mensajes fallidos", "Los mensajes que no se pudieron procesar, para reintentarlos.",
            "/backoffice/mensajes-fallidos", [Administrador]),
    ];

    // El menú es para el personal del operador. Un usuario de comercio no lo ve aunque tenga otro perfil.
    public static bool MuestraMenu(ClaimsPrincipal usuario) =>
        usuario.Identity?.IsAuthenticated == true && !usuario.IsInRole("UsuarioComercio")
        && Visibles(usuario).Any();

    public static IEnumerable<IGrouping<string, SeccionBackoffice>> Visibles(ClaimsPrincipal usuario) =>
        Todas.Where(s => s.Perfiles.Any(usuario.IsInRole)).GroupBy(s => s.Grupo);

    // Una sección queda marcada también en sus páginas internas (el detalle de un envío marca "Envíos").
    public static bool EsActual(string ruta, PathString actual) =>
        actual.StartsWithSegments(ruta, StringComparison.OrdinalIgnoreCase);
}

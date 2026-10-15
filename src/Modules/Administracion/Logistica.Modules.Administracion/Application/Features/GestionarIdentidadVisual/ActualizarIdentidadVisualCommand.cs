namespace Logistica.Modules.Administracion.Application.Features.GestionarIdentidadVisual;

// Lo que manda el administrador (CU-07, paso 3). El operador no viene acá: es el de la sesión.
internal sealed record ActualizarIdentidadVisualCommand(
    string ColorPrimario,
    string ColorSecundario,
    string? EmailContacto,
    string? TelefonoContacto,
    LogoSubido? Logo);

// null en el command: se conserva el logo actual.
internal sealed record LogoSubido(string TipoContenido, byte[] Contenido);

// A3: el contraste insuficiente no impide guardar; se le avisa al administrador.
internal sealed record ResultadoIdentidadVisual(bool ContrasteInsuficiente);

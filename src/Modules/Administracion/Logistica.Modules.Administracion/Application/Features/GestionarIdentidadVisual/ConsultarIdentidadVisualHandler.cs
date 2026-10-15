using Logistica.Modules.Administracion.Domain.Operadores;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.GestionarIdentidadVisual;

internal sealed record IdentidadVisualDto(
    string NombreOperador,
    string ColorPrimario,
    string ColorSecundario,
    Guid? LogoArchivoId,
    string? EmailContacto,
    string? TelefonoContacto);

// La identidad del operador de la sesión: la muestra la página de CU-07 y la usan los estilos de cada operador.
internal sealed class ConsultarIdentidadVisualHandler(IOperadorRepository operadores, ICurrentTenant tenant)
{
    // null: la sesión no tiene operador (por ejemplo, un comercio que todavía no eligió, CU-03).
    public async Task<IdentidadVisualDto?> HandleAsync(CancellationToken ct)
    {
        if (tenant.OperadorId is not Guid operadorId || await operadores.ObtenerAsync(operadorId, ct) is not Operador operador)
        {
            return null;
        }

        var identidad = operador.Identidad;
        return new IdentidadVisualDto(operador.Nombre, identidad.ColorPrimario, identidad.ColorSecundario,
            identidad.LogoArchivoId, identidad.EmailContacto, identidad.TelefonoContacto);
    }

    // El logo del operador de la sesión. null: sin operador o sin logo.
    public async Task<LogoDto?> LogoAsync(CancellationToken ct)
    {
        if (await HandleAsync(ct) is not { LogoArchivoId: Guid logoId }
            || await operadores.ObtenerLogoAsync(logoId, ct) is not LogoOperador logo)
        {
            return null;
        }

        return new LogoDto(logo.TipoContenido, logo.Contenido);
    }
}

internal sealed record LogoDto(string TipoContenido, byte[] Contenido);

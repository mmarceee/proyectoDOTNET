using Logistica.Modules.Administracion.Contracts;

namespace Logistica.Modules.Planificacion.Application.Services;

internal sealed class CalendarioPlanificacion(IAdministracionModuleApi administracion, TimeProvider reloj)
{
    public async Task<DateOnly> HoyAsync(CancellationToken ct)
    {
        var contexto = await administracion.ObtenerContextoPlanificacionAsync(ct);
        var zona = TimeZoneInfo.FindSystemTimeZoneById(contexto.ZonaHoraria);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zona).DateTime);
    }
}

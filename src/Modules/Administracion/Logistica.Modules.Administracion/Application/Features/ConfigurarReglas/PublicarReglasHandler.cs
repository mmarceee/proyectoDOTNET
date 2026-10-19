using Logistica.Modules.Administracion.Application.Features.ConsultarRecursosPlanificacion;
using Logistica.Modules.Administracion.Domain.Reglas;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.ConfigurarReglas;

// CU-06, pasos 5 y 6: publicar el borrador de inmediato o desde un día futuro. A1, A2 y la fecha pasada los valida
// VersionReglas; la versión anterior se cierra en el mismo momento y, después de guardar, se invalida la caché de reglas
// (ADR-0006): los envíos que se admitan desde ahora guardan la versión nueva.
internal sealed class PublicarReglasHandler(
    IReglasRepository versiones, IRecursosPlanificacionReader recursos, ReglasVigentes reglas, TimeProvider reloj)
{
    // fecha null: de inmediato. Una fecha es el comienzo de ese día en la zona horaria del operador.
    public async Task PublicarAsync(DateOnly? fecha, CancellationToken ct)
    {
        // A3: con un solo borrador por operador, si no está es porque otro administrador lo publicó.
        var borrador = await versiones.ObtenerBorradorAsync(ct)
            ?? throw new DomainException("No hay un borrador para publicar: puede que otro administrador lo haya publicado. Revisá las versiones.");

        var ahora = reloj.GetUtcNow();
        borrador.Publicar(await DesdeAsync(fecha, ahora, ct), ahora, await versiones.UltimaPublicadaAsync(ct));
        await versiones.GuardarCambiosAsync(ct);
        await reglas.InvalidarAsync(ct);
    }

    // Igual que el tarifario: de inmediato es "ahora"; un día es su comienzo en la zona horaria del operador, en UTC, y
    // si es hoy rige ya.
    private async Task<DateTimeOffset> DesdeAsync(DateOnly? fecha, DateTimeOffset ahora, CancellationToken ct)
    {
        if (fecha is not DateOnly dia)
        {
            return ahora;
        }

        var zonaHoraria = TimeZoneInfo.FindSystemTimeZoneById((await recursos.ObtenerContextoPlanificacionAsync(ct)).ZonaHoraria);
        var comienzo = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(dia.ToDateTime(TimeOnly.MinValue), zonaHoraria), TimeSpan.Zero);
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, zonaHoraria).DateTime);
        return dia == hoy ? ahora : comienzo;
    }
}

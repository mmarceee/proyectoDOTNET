using System.Globalization;
using Logistica.Modules.Administracion.Application.Features.ConsultarRecursosPlanificacion;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Planificacion;
using Logistica.Modules.Administracion.Domain.Tarifas;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.ConfigurarTarifario;

// A2: el resultado cuando faltan precios y el administrador todavía no confirmó.
internal sealed record ZonaSinTarifaDto(string Zona, Modalidad Modalidad);

internal sealed record ResultadoPublicacion(bool Publicada, IReadOnlyList<ZonaSinTarifaDto> SinTarifa);

// CU-05, pasos 7 a 9: publicar el borrador de inmediato o desde un día futuro. Valida A1 y A3 (en el dominio), advierte A2
// y pide confirmación, cierra la versión anterior en el mismo momento y, después de guardar, invalida la caché del
// cuadro tarifario (ADR-0006): la próxima lectura ya usa la versión nueva.
internal sealed class PublicarTarifarioHandler(
    ITarifarioRepository versiones, IZonaRepository zonas, IRecursosPlanificacionReader recursos,
    TarifarioVigente tarifario, TimeProvider reloj)
{
    // fecha null: de inmediato. Una fecha es el comienzo de ese día en la zona horaria del operador.
    public async Task<ResultadoPublicacion> PublicarAsync(DateOnly? fecha, bool confirmaSinTarifa, CancellationToken ct)
    {
        // A5: con un solo borrador por operador, si no está es porque otro administrador lo publicó.
        var borrador = await versiones.ObtenerBorradorAsync(ct)
            ?? throw new DomainException("No hay un borrador para publicar: puede que otro administrador lo haya publicado. Revisá las versiones.");

        // A1: se indican las reglas en conflicto y no se publica.
        var conflictos = borrador.Conflictos();
        var activas = (await zonas.ListarAsync(ct)).Where(z => z.Activa).ToList();
        if (conflictos.Count > 0)
        {
            throw new DomainException("Hay reglas superpuestas, un bulto tendría dos precios: "
                + string.Join("; ", conflictos.Select(c => $"{Describir(c.Una, activas)} con {Describir(c.Otra, activas)}")) + ".");
        }

        // A2: se advierte y se pide confirmación; con la confirmación, se publica igual.
        var sinTarifa = borrador.CombinacionesSinTarifa(activas.Select(z => z.Id))
            .Select(c => new ZonaSinTarifaDto(NombreZona(c.ZonaId, activas), c.Modalidad)).ToList();
        if (sinTarifa.Count > 0 && !confirmaSinTarifa)
        {
            return new ResultadoPublicacion(false, sinTarifa);
        }

        var ahora = reloj.GetUtcNow();
        borrador.Publicar(await DesdeAsync(fecha, ahora, ct), ahora, await versiones.UltimaPublicadaAsync(ct));
        await versiones.GuardarCambiosAsync(ct);
        await tarifario.InvalidarAsync(ct);
        return new ResultadoPublicacion(true, []);
    }

    // De inmediato es "ahora". Un día es su comienzo en la zona horaria del operador, en UTC; si es hoy, rige ya (A3 no
    // rechaza hoy, sólo los días pasados).
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

    private static string Describir(ReglaTarifa regla, IReadOnlyList<Zona> zonas) => string.Create(CultureInfo.InvariantCulture,
        $"{NombreZona(regla.ZonaId, zonas)} {regla.Modalidad} ({regla.PesoDesdeKg}–{regla.PesoHastaKg} kg, {regla.VolumenDesdeM3}–{regla.VolumenHastaM3} m³)");

    private static string NombreZona(Guid zonaId, IReadOnlyList<Zona> zonas) =>
        zonas.FirstOrDefault(z => z.Id == zonaId)?.Codigo ?? "zona inactiva";
}

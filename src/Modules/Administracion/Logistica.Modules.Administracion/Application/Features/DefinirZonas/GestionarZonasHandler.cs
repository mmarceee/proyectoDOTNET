using Logistica.Modules.Administracion.Application.Features.ConsultarRecursosPlanificacion;
using Logistica.Modules.Administracion.Domain.Planificacion;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.DefinirZonas;

// CU-04: alta, modificación (A4) y desactivación (A5) de zonas, y alta y reemplazo de franjas. Coordina: las reglas de
// una zona o una franja sola las validan Zona y FranjaHoraria; acá van las que comparan con las demás (A1, A2 y A3).
// Primero se valida y después se modifica: si una regla falla, las entidades en seguimiento quedan como estaban y la
// página no muestra datos que no se guardaron.
// Si la zona no es del operador de la sesión, el filtro la oculta y se devuelve false (como si no existiera).
internal sealed class GestionarZonasHandler(
    IZonaRepository zonas, ICurrentTenant tenant, IEnviosModuleApi envios, IPlanificacionModuleApi planificacion,
    IRecursosPlanificacionReader recursos, IUnidadDeTrabajo unidad, TimeProvider reloj)
{
    public async Task<Guid> CrearAsync(string codigo, string nombre, string[] postales, CancellationToken ct)
    {
        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        var zona = Zona.Crear(operadorId, codigo, nombre, postales);

        // A1: el código es único en el operador. Ante dos altas simultáneas, el índice único (OperadorId, Codigo) frena
        // la segunda y el repositorio la informa con el mismo mensaje.
        if (await zonas.ExisteCodigoAsync(zona.Codigo, ct))
        {
            throw new DomainException($"Ya existe una zona con el código {zona.Codigo}.");
        }

        await ExigirCodigosPostalesLibresAsync(zona.Id, zona.CodigosPostales, ct);

        zonas.Agregar(zona);
        await zonas.GuardarCambiosAsync(ct);
        return zona.Id;
    }

    public async Task<bool> ModificarAsync(Guid id, string nombre, string[] postales, CancellationToken ct)
    {
        if (await zonas.ObtenerAsync(id, ct) is not Zona zona)
        {
            return false;
        }

        await ExigirCodigosPostalesLibresAsync(zona.Id, postales, ct);
        zona.Modificar(nombre, postales);
        await zonas.GuardarCambiosAsync(ct);
        return true;
    }

    public async Task<bool> DesactivarAsync(Guid id, CancellationToken ct)
    {
        if (await zonas.ObtenerAsync(id, ct) is not Zona zona)
        {
            return false;
        }

        zona.Desactivar();
        await zonas.GuardarCambiosAsync(ct);
        return true;
    }

    public async Task<bool> AgregarFranjaAsync(Guid zonaId, TimeOnly desde, TimeOnly hasta, DayOfWeek[] dias,
        DateOnly vigenteDesde, CancellationToken ct)
    {
        if (await zonas.ObtenerAsync(zonaId, ct) is not Zona zona)
        {
            return false;
        }

        // Una franja nueva no cambia horarios ya prometidos, pero tampoco tiene sentido que empiece en el pasado.
        if (vigenteDesde < await HoyAsync(ct))
        {
            throw new DomainException("La franja tiene que regir desde hoy o después.");
        }

        var franja = FranjaHoraria.Crear(zona.OperadorId, zona.Id, desde, hasta, dias, vigenteDesde);
        ExigirSinSuperposicion(await zonas.FranjasDeAsync(zona.Id, ct), franja);

        zonas.AgregarFranja(franja);
        await zonas.GuardarCambiosAsync(ct);
        return true;
    }

    // A4 con franjas: la anterior rige hasta el día antes de la fecha indicada y la nueva desde esa fecha, conservando
    // el historial. La reprogramación de los envíos afectados (ampliación del 09/10) queda pendiente de CU-04: mientras
    // tanto, si hay envíos o rutas comprometidos con la franja desde esa fecha, el cambio se bloquea (acordado con
    // Lucas, 18 y 19/10).
    public async Task<bool> ReemplazarFranjaAsync(Guid zonaId, Guid franjaId, TimeOnly desde, TimeOnly hasta,
        DayOfWeek[] dias, DateOnly desdeFecha, CancellationToken ct)
    {
        var reemplazada = false;

        // El mismo bloqueo de planificación del operador que toman armar, modificar y despachar rutas: nadie asigna
        // envíos a una ruta entre la comprobación y el guardado. Si algo falla, la transacción no se confirma.
        await unidad.EjecutarAsync(async () =>
        {
            await recursos.BloquearPlanificacionAsync(ct);

            var existentes = await zonas.FranjasDeAsync(zonaId, ct);
            if (existentes.FirstOrDefault(f => f.Id == franjaId) is not FranjaHoraria anterior)
            {
                return;
            }

            await ExigirSinCompromisosAsync(CadenaDeReemplazos(existentes, anterior), desdeFecha, ct);

            // A3 antes de tocar la anterior: la nueva se compara con las demás franjas. Con la anterior no puede
            // chocar, porque termina el día antes de que empiece la nueva.
            var propuesta = FranjaHoraria.Crear(anterior.OperadorId, anterior.ZonaId, desde, hasta, dias, desdeFecha);
            ExigirSinSuperposicion(existentes.Where(f => f.Id != anterior.Id).ToList(), propuesta);

            var nueva = anterior.ProgramarCambio(desde, hasta, dias, desdeFecha, await HoyAsync(ct));
            zonas.AgregarFranja(nueva);
            await zonas.GuardarCambiosAsync(ct);
            reemplazada = true;
        }, ct);

        return reemplazada;
    }

    // El día de hoy del operador, en su zona horaria: el servidor puede estar en UTC.
    public async Task<DateOnly> HoyAsync(CancellationToken ct)
    {
        var contexto = await recursos.ObtenerContextoPlanificacionAsync(ct);
        var zonaHoraria = TimeZoneInfo.FindSystemTimeZoneById(contexto.ZonaHoraria);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reloj.GetUtcNow(), zonaHoraria).DateTime);
    }

    // Planificación sigue los reemplazos por fecha: un envío con esta franja, o con una que esta reemplazó, tomaría el
    // horario nuevo sin aviso ni revalidación. Se mira la fecha del envío y, si no tiene, la de la ruta donde está.
    private async Task ExigirSinCompromisosAsync(IReadOnlyCollection<Guid> cadena, DateOnly desde, CancellationToken ct)
    {
        if (await envios.HayEnviosComprometidosAsync(cadena, desde, ct)
            || await planificacion.HayRutasActivasConEnviosAsync(await envios.EnviosSinFechaConFranjasAsync(cadena, ct), desde, ct))
        {
            throw new DomainException(
                "Hay envíos o rutas comprometidos con esta franja desde esa fecha. Elegí una fecha posterior a sus entregas.");
        }
    }

    // A2: cada código postal pertenece a una sola zona activa del operador, para que el alta de envíos determine la
    // zona sin ambigüedad. Compara con las demás zonas: por eso va acá y no en Zona.
    private async Task ExigirCodigosPostalesLibresAsync(Guid zonaId, IEnumerable<string> postales, CancellationToken ct)
    {
        var otras = (await zonas.ListarAsync(ct)).Where(z => z.Activa && z.Id != zonaId).ToList();
        foreach (var postal in postales.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()))
        {
            if (otras.FirstOrDefault(z => z.Cubre(postal)) is Zona ocupada)
            {
                throw new DomainException($"El código postal {postal} ya pertenece a la zona {ocupada.Codigo}.");
            }
        }
    }

    // La franja y las que fue reemplazando, hacia atrás: todas terminan resolviéndose en ella.
    private static List<Guid> CadenaDeReemplazos(IReadOnlyList<FranjaHoraria> existentes, FranjaHoraria ultima)
    {
        var cadena = new List<Guid> { ultima.Id };
        for (var actual = ultima; actual.ReemplazaAId is Guid previa
             && existentes.FirstOrDefault(f => f.Id == previa) is FranjaHoraria anterior; actual = anterior)
        {
            cadena.Add(anterior.Id);
        }

        return cadena;
    }

    // A3: la regla de superposición es de FranjaHoraria; acá sólo se compara con las demás franjas de la zona.
    private static void ExigirSinSuperposicion(IReadOnlyList<FranjaHoraria> existentes, FranjaHoraria nueva)
    {
        if (existentes.Any(f => f.SeSuperponeCon(nueva)))
        {
            throw new DomainException("La franja se superpone con otra de la zona en el mismo día.");
        }
    }
}

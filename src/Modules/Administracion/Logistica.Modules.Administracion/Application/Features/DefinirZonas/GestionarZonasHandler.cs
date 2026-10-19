using Logistica.Modules.Administracion.Domain.Planificacion;
using Logistica.Modules.Envios.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.DefinirZonas;

// CU-04: alta, modificación (A4) y desactivación (A5) de zonas, y alta y reemplazo de franjas. Coordina: las reglas de
// una zona o una franja sola las validan Zona y FranjaHoraria; acá van las que comparan con las demás (A1, A2 y A3).
// Si la zona no es del operador de la sesión, el filtro la oculta y se devuelve false (como si no existiera).
internal sealed class GestionarZonasHandler(IZonaRepository zonas, ICurrentTenant tenant, IEnviosModuleApi envios)
{
    public async Task<Guid> CrearAsync(string codigo, string nombre, string[] postales, CancellationToken ct)
    {
        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        var zona = Zona.Crear(operadorId, codigo, nombre, postales);

        // A1: el código es único en el operador. El índice único (OperadorId, Codigo) protege además ante dos altas
        // simultáneas.
        if (await zonas.ExisteCodigoAsync(zona.Codigo, ct))
        {
            throw new DomainException($"Ya existe una zona con el código {zona.Codigo}.");
        }

        await ExigirCodigosPostalesLibresAsync(zona, ct);

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

        zona.Modificar(nombre, postales);
        await ExigirCodigosPostalesLibresAsync(zona, ct);
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

        var franja = FranjaHoraria.Crear(zona.OperadorId, zona.Id, desde, hasta, dias, vigenteDesde);
        ExigirSinSuperposicion(await zonas.FranjasDeAsync(zona.Id, ct), franja);

        zonas.AgregarFranja(franja);
        await zonas.GuardarCambiosAsync(ct);
        return true;
    }

    // A4 con franjas: la anterior rige hasta el día antes de la fecha indicada y la nueva desde esa fecha, conservando
    // el historial. La reprogramación de los envíos afectados (ampliación del 09/10) queda pendiente de CU-04: mientras
    // tanto, si hay envíos comprometidos con la franja desde esa fecha, el cambio se bloquea (acordado con Lucas, 18/10).
    public async Task<bool> ReemplazarFranjaAsync(Guid zonaId, Guid franjaId, TimeOnly desde, TimeOnly hasta,
        DayOfWeek[] dias, DateOnly desdeFecha, CancellationToken ct)
    {
        var existentes = await zonas.FranjasDeAsync(zonaId, ct);
        if (existentes.FirstOrDefault(f => f.Id == franjaId) is not FranjaHoraria anterior)
        {
            return false;
        }

        // Planificación sigue los reemplazos por fecha: un envío con esta franja, o con una que esta reemplazó, tomaría
        // el horario nuevo sin aviso ni revalidación. Envíos los conoce y las rutas los incluyen.
        if (await envios.HayEnviosComprometidosAsync(CadenaDeReemplazos(existentes, anterior), desdeFecha, ct))
        {
            throw new DomainException(
                "Hay envíos comprometidos con esta franja desde esa fecha. Elegí una fecha posterior a sus entregas.");
        }

        var nueva = anterior.Reemplazar(desde, hasta, dias, desdeFecha);
        ExigirSinSuperposicion(existentes, nueva);

        zonas.AgregarFranja(nueva);
        await zonas.GuardarCambiosAsync(ct);
        return true;
    }

    // A2: cada código postal pertenece a una sola zona activa del operador, para que el alta de envíos determine la
    // zona sin ambigüedad. Compara con las demás zonas: por eso va acá y no en Zona.
    private async Task ExigirCodigosPostalesLibresAsync(Zona zona, CancellationToken ct)
    {
        var otras = (await zonas.ListarAsync(ct)).Where(z => z.Activa && z.Id != zona.Id).ToList();
        foreach (var postal in zona.CodigosPostales)
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

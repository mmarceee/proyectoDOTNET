using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.Modules.Administracion.Application.Features.ConsultarRecursosPlanificacion;
using Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application;

internal sealed class AdministracionModuleApi(IRecursosPlanificacionReader lector, IServicioUsuarios usuarios,
    ICurrentTenant tenant) : IAdministracionModuleApi
{
    public Task BloquearPlanificacionAsync(CancellationToken ct) => lector.BloquearPlanificacionAsync(ct);
    public Task<ContextoPlanificacion> ObtenerContextoPlanificacionAsync(CancellationToken ct) => lector.ObtenerContextoPlanificacionAsync(ct);
    public Task<ReglasPlanificacion> ObtenerReglasPlanificacionAsync(DateTimeOffset ahora, CancellationToken ct) => lector.ObtenerReglasPlanificacionAsync(ahora, ct);
    public async Task<RecursosPlanificacion> ConsultarRecursosPlanificacionAsync(DateOnly fecha, CancellationToken ct)
    {
        var datos = await lector.LeerRecursosAsync(ct);
        var repartidores = datos.Repartidores; var vehiculos = datos.Vehiculos;
        var zonas = datos.Zonas; var franjas = datos.Franjas;
        var operador = tenant.OperadorId ?? throw new DomainException("La sesión no tiene un operador.");
        var habilitados = await usuarios.UsuariosRepartidoresHabilitadosAsync(operador,
            repartidores.Where(r => r.UsuarioId.HasValue).Select(r => r.UsuarioId!.Value).Distinct().ToArray(), ct);
        return new(repartidores.Select(r => new RepartidorPlanificacion(r.Id, r.Nombre, r.Activo, r.UsuarioId.HasValue,
                r.UsuarioId is Guid usuarioId && habilitados.Contains(usuarioId))).ToList(),
            vehiculos.Select(v => new VehiculoPlanificacion(v.Id, v.Matricula, v.Activo, v.CapacidadPesoKg,
                v.CapacidadVolumenM3, v.LargoCargaCm, v.AnchoCargaCm, v.AltoCargaCm)).ToList(),
            zonas.Select(z => new ZonaPlanificacion(z.Id, z.Nombre, z.CodigosPostales)).ToList(),
            franjas.Where(f => f.Aplica(fecha) && zonas.Any(z => z.Id == f.ZonaId)).Select(f => MapearFranja(f.Id, f)).ToList());
    }
    public async Task<IReadOnlyList<FranjaPlanificacion>> ResolverFranjasAsync(IReadOnlyCollection<Guid> ids, DateOnly fecha, CancellationToken ct)
    {
        var datos = await lector.LeerRecursosAsync(ct);
        var zonasActivas = datos.Zonas.Select(z => z.Id).ToList();
        var franjas = datos.Franjas;
        var resultado = new List<FranjaPlanificacion>();
        foreach (var id in ids.Distinct())
        {
            var franja = franjas.SingleOrDefault(f => f.Id == id);
            var visitadas = new HashSet<Guid>();
            while (franja is not null && visitadas.Add(franja.Id))
            {
                if (franja.Aplica(fecha) && zonasActivas.Contains(franja.ZonaId)) { resultado.Add(MapearFranja(id, franja)); break; }
                if (fecha < franja.VigenteDesde) break;
                franja = franjas.SingleOrDefault(f => f.ReemplazaAId == franja.Id);
            }
        }
        return resultado;
    }
    private static FranjaPlanificacion MapearFranja(Guid referencia, Domain.Planificacion.FranjaHoraria f)
        => new(referencia, f.Id, f.ZonaId, f.HoraDesde, f.HoraHasta, f.Dias.Select(d => (DayOfWeek)d).ToArray(), f.VigenteDesde, f.VigenteHasta);
}

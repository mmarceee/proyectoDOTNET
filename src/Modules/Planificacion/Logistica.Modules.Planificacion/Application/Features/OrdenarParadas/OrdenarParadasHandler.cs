using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Planificacion.Application.Abstractions;
using Logistica.Modules.Planificacion.Application.Exceptions;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.SharedKernel;
using static Logistica.Modules.Planificacion.Application.Mapping.RutaResponseMapper;

namespace Logistica.Modules.Planificacion.Application.Features.OrdenarParadas;

internal sealed class OrdenarParadasHandler(IRutaRepository rutas, IEnviosModuleApi envios,
    IAdministracionModuleApi administracion, IConfirmacionPlanificacion confirmacion)
{
    public async Task<OrdenParadasResponse> ConsultarAsync(Guid id, CancellationToken ct)
    {
        var ruta = await ExigirRutaAsync(id, null, ct);
        return await VistaAsync(ruta, ruta.Paradas.OrderBy(p => p.Orden).Select(p => p.Id).ToArray(), ct);
    }

    public async Task<OrdenParadasResponse> ProponerAsync(Guid id, long revision, CancellationToken ct)
    {
        var ruta = await ExigirRutaAsync(id, revision, ct);
        var claves = await ClavesAsync(ruta, ct);
        return Vista(ruta, OrdenadorParadas.Proponer(ruta.Paradas, claves), claves);
    }

    public async Task<OrdenParadasResponse> RevisarAsync(Guid id, long revision, IReadOnlyList<Guid> orden, CancellationToken ct)
    {
        var ruta = await ExigirRutaAsync(id, revision, ct);
        return await VistaAsync(ruta, orden, ct);
    }

    public async Task<RutaResponse> GuardarAsync(Guid id, long revision, IReadOnlyList<Guid> orden,
        bool confirmarAdvertencias, CancellationToken ct)
    {
        RutaResponse? respuesta = null;
        await confirmacion.EjecutarAsync(async () =>
        {
            // Mismo bloqueo por operador que agregar, quitar, replanificar y despachar.
            await administracion.BloquearPlanificacionAsync(ct);
            var ruta = await ExigirRutaAsync(id, revision, ct);
            OrdenadorParadas.ExigirPermutacion(ruta.Paradas.Select(p => p.Id).ToArray(), orden);
            var porId = ruta.Paradas.ToDictionary(p => p.Id);
            if (OrdenadorParadas.Advertir(orden.Select(p => porId[p]).ToArray()).Count > 0 && !confirmarAdvertencias)
                throw new DomainException("El orden puede incumplir franjas horarias. Revisá la advertencia y confirmá para guardarlo igualmente.");
            ruta.OrdenarParadas(orden);
            await rutas.GuardarAsync(ct);
            respuesta = Mapear(ruta);
        }, [], ct);
        return respuesta!;
    }

    private async Task<Ruta> ExigirRutaAsync(Guid id, long? revision, CancellationToken ct)
    {
        if (id == Guid.Empty || revision is < 1) throw new DomainException("La ruta y la revisión son obligatorias.");
        var ruta = await rutas.ObtenerAsync(id, ct) ?? throw new RutaNoEncontradaException();
        if (ruta.Estado != EstadoRuta.Planificada)
            throw new ConflictoRutaException("RutaNoEditable", "Sólo se pueden ordenar paradas de una ruta Planificada.");
        if (revision is long esperada && ruta.Revision != esperada)
            throw new ConflictoRutaException("RutaModificada", "Otra operación modificó la ruta. Recargá y revisá nuevamente el orden.", revision: ruta.Revision);
        if (ruta.Paradas.Count == 0) throw new DomainException("La ruta debe tener paradas para ordenarlas.");
        return ruta;
    }

    private async Task<IReadOnlyList<ClaveOrdenParada>> ClavesAsync(Ruta ruta, CancellationToken ct)
    {
        // Sólo datos geográficos de los contratos; no se consultan estados de bulto ni se reagrupan visitas.
        var lista = await envios.ObtenerParaPlanificacionAsync(ruta.EnvioIds.ToArray(), ct);
        if (lista.Count != ruta.EnvioIds.Count())
            throw new ConflictoRutaException("EnvioNoDisponible", "No se pudieron leer todos los envíos de las paradas.");
        var zonas = (await administracion.ConsultarRecursosPlanificacionAsync(ruta.Fecha, ct)).Zonas;
        return ruta.Paradas.Select(p =>
        {
            var miembros = p.Envios.Select(e => lista.Single(d => d.Id == e.EnvioId)).ToArray();
            var nombres = miembros.Select(e => e.ZonaId is Guid zona
                ? zonas.SingleOrDefault(z => z.Id == zona)?.Nombre ?? "Zona no disponible"
                : zonas.FirstOrDefault(z => z.CodigosPostales.Contains(e.CodigoPostal))?.Nombre ?? "")
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
            return new ClaveOrdenParada(p.Id, string.Join(" / ", nombres),
                miembros.Select(e => e.CodigoPostal).Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? "");
        }).ToArray();
    }

    private async Task<OrdenParadasResponse> VistaAsync(Ruta ruta, IReadOnlyList<Guid> orden, CancellationToken ct)
    {
        OrdenadorParadas.ExigirPermutacion(ruta.Paradas.Select(p => p.Id).ToArray(), orden);
        return Vista(ruta, orden, await ClavesAsync(ruta, ct));
    }

    private static OrdenParadasResponse Vista(Ruta ruta, IReadOnlyList<Guid> orden, IReadOnlyList<ClaveOrdenParada> claves)
    {
        var paradas = ruta.Paradas.ToDictionary(p => p.Id);
        var datos = claves.ToDictionary(c => c.ParadaId);
        var avisos = OrdenadorParadas.Advertir(orden.Select(id => paradas[id]).ToArray())
            .Select(a => new AdvertenciaOrdenResponse(a.ParadaId,
                $"La parada de {paradas[a.ParadaId].Direccion} quedaría como mínimo a las {a.HoraMinima:HH:mm}, fuera de su franja que termina a las {a.FinFranja:HH:mm}, aun sin contar traslados ni duración de visitas."))
            .ToArray();
        return new(ruta.Id, ruta.Revision, orden.Select((id, i) => new ParadaOrdenResponse(id, i + 1,
            paradas[id].Direccion, paradas[id].Desde, paradas[id].Hasta, datos[id].Zona, datos[id].CodigoPostal)).ToArray(), avisos);
    }
}

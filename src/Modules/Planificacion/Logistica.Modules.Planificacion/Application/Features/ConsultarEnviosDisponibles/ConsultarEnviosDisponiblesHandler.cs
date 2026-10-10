using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Application.Services;
using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Application.Features.ConsultarEnviosDisponibles;

internal sealed class ConsultarEnviosDisponiblesHandler(IRutaRepository rutas, IAdministracionModuleApi administracion, IEnviosModuleApi envios, PreparadorEnviosRuta preparador)
{
    public async Task<EnviosDisponiblesResponse> DisponiblesAsync(DateOnly fecha, Guid? zona, Guid? franja,
        string? texto, int pagina, int tamano, CancellationToken ct, IReadOnlyCollection<Guid>? seleccion = null)
    {
        if (pagina < 1 || tamano is < 1 or > 100) throw new DomainException("La paginación debe estar entre 1 y 100 elementos.");
        texto = texto?.Trim();
        var candidatos = await envios.ObtenerParaPlanificacionAsync(null, ct);
        var ocupados = (await rutas.EnviosOcupadosAsync(candidatos.Select(e => e.Id).ToArray(), null, ct)).ToHashSet();
        var recursos = await administracion.ConsultarRecursosPlanificacionAsync(fecha, ct);
        var preparados = await preparador.PrepararAsync(candidatos, fecha, recursos, ct);
        var lista = candidatos.Where(e => !ocupados.Contains(e.Id) && (e.FechaEntregaProgramada is null || e.FechaEntregaProgramada == fecha))
            .Where(e => seleccion is null || seleccion.Contains(e.Id))
            .Select(e => (Envio: e, Datos: preparados.Single(p => p.Id == e.Id)))
            .Where(p => !p.Datos.FranjaRequerida || p.Datos.Franja is not null)
            .Where(p => zona is null || p.Datos.ZonaId == zona)
            .Where(p => franja is null || p.Datos.Franja?.Id == franja)
            .Where(p => string.IsNullOrWhiteSpace(texto) || p.Envio.Numero.Contains(texto, StringComparison.OrdinalIgnoreCase)
                || p.Envio.Direccion.Contains(texto, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Envio.Numero).ToList();
        return new(lista.Skip((pagina - 1) * tamano).Take(tamano).Select(p => new EnvioDisponibleResponse(p.Envio.Id,
            p.Envio.Numero, p.Envio.Direccion, p.Datos.ZonaId, p.Datos.Franja?.Id, p.Envio.FechaEntregaProgramada,
            p.Datos.Bultos.Sum(b => b.PesoKg), p.Datos.Bultos.Sum(b => b.VolumenM3))).ToList(), lista.Count, pagina, tamano);
    }
}

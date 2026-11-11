using Logistica.Http.Contracts.Planificacion;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Application.Services;
using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Application.Features.ConsultarEnviosDisponibles;

internal sealed class ConsultarEnviosDisponiblesHandler(IAdministracionModuleApi administracion, IEnviosModuleApi envios, PreparadorEnviosRuta preparador,
    IReservaBultoRepository reservas)
{
    public async Task<EnviosDisponiblesResponse> DisponiblesAsync(DateOnly fecha, Guid? zona, Guid? franja,
        string? texto, int pagina, int tamano, CancellationToken ct, IReadOnlyCollection<Guid>? seleccion = null)
    {
        if (pagina < 1 || tamano is < 1 or > 100) throw new DomainException("La paginación debe estar entre 1 y 100 elementos.");
        texto = texto?.Trim();
        var candidatos = await envios.ObtenerParaPlanificacionAsync(null, ct);
        var ocupados = (await reservas.OcupadosAsync(candidatos.SelectMany(e => e.Bultos).Select(b => b.Id).ToArray(), null, ct)).ToHashSet();
        candidatos = candidatos.Select(e => e with { Bultos = e.Bultos.Where(b => b.Estado is "EnDeposito" or "Reprogramado"
            && b.Custodia == "Deposito" && !ocupados.Contains(b.Id)).ToArray() }).Where(e => e.Bultos.Count > 0).ToArray();
        candidatos = candidatos.Select(e => e with { ReintentoDesde = e.Bultos.Max(b => b.ReintentoDesde) }).ToArray();
        var recursos = await administracion.ConsultarRecursosPlanificacionAsync(fecha, ct);
        var preparados = await preparador.PrepararAsync(candidatos, fecha, recursos, ct);
        var lista = candidatos.Where(e => e.FechaEntregaProgramada is null || e.FechaEntregaProgramada == fecha)
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
            p.Datos.Bultos.Sum(b => b.PesoKg), p.Datos.Bultos.Sum(b => b.VolumenM3),
            p.Datos.ReintentoDesde, p.Datos.CumplePlazoEntreIntentos, p.Envio.Modalidad)
            { Bultos = p.Envio.Bultos.Select(b => new BultoDisponibleResponse(b.Id, b.Codigo, b.Estado, b.PesoKg,
                b.RevisionCustodia)).ToArray() }).ToList(), lista.Count, pagina, tamano);
    }
}

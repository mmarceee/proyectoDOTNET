using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal sealed record ClaveOrdenParada(Guid ParadaId, string Zona, string CodigoPostal);
internal sealed record AdvertenciaOrden(Guid ParadaId, TimeOnly HoraMinima, TimeOnly FinFranja);

// CU-42: criterio fijo, sin tiempos de viaje ni optimización geográfica.
internal static class OrdenadorParadas
{
    public static IReadOnlyList<Guid> Proponer(IReadOnlyList<Parada> paradas, IReadOnlyList<ClaveOrdenParada> claves)
    {
        ExigirPermutacion(paradas.Select(p => p.Id).ToArray(), claves.Select(c => c.ParadaId).ToArray());
        var datos = claves.ToDictionary(c => c.ParadaId);
        return paradas.OrderBy(p => p.Desde is null).ThenBy(p => p.Desde).ThenBy(p => p.Hasta)
            .ThenBy(p => string.IsNullOrWhiteSpace(datos[p.Id].Zona))
            .ThenBy(p => datos[p.Id].Zona.Trim(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => datos[p.Id].CodigoPostal.Trim(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Orden).ThenBy(p => p.Id).Select(p => p.Id).ToArray();
    }

    public static void ExigirPermutacion(IReadOnlyList<Guid> actuales, IReadOnlyList<Guid> orden)
    {
        if (actuales.Count == 0) throw new DomainException("La ruta debe tener paradas para ordenarlas.");
        if (orden.Count != actuales.Count || orden.Any(id => id == Guid.Empty)
            || orden.Distinct().Count() != orden.Count || orden.Except(actuales).Any())
            throw new DomainException("El orden debe contener todas las paradas de la ruta, una sola vez y sin paradas ajenas.");
    }

    // Cota inferior: se espera hasta el inicio de cada franja, sin sumar traslados ni duración de visitas.
    // Si aun así se alcanza el fin de una franja, el orden es incompatible. No certifica puntualidad.
    public static IReadOnlyList<AdvertenciaOrden> Advertir(IReadOnlyList<Parada> orden)
    {
        var hora = TimeOnly.MinValue;
        var resultado = new List<AdvertenciaOrden>();
        foreach (var parada in orden)
        {
            if (parada.Desde is TimeOnly desde && desde > hora) hora = desde;
            if (parada.Hasta is TimeOnly hasta && hora >= hasta)
                resultado.Add(new(parada.Id, hora, hasta));
        }
        return resultado;
    }
}

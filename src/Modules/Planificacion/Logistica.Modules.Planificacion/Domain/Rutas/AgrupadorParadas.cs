namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal static class AgrupadorParadas
{
    public static IReadOnlyList<GrupoParada> Agrupar(IReadOnlyList<EnvioRuta> envios)
    {
        var resultado = new List<GrupoParada>();
        foreach (var direccion in envios.GroupBy(e => e.Punto?.Clave ?? e.Id.ToString()))
        {
            var grupos = new List<GrupoParada>();
            // Primero las franjas: los envíos sin franja se agregan a una sola visita, la más temprana.
            foreach (var envio in direccion.OrderBy(e => e.Franja is null ? 1 : 0)
                .ThenBy(e => e.Franja?.Hasta).ThenBy(e => e.Franja?.Desde))
            {
                var grupo = grupos.FirstOrDefault(g => envio.Franja is null || g.Desde is null
                    || Max(g.Desde.Value, envio.Franja.Desde) < Min(g.Hasta!.Value, envio.Franja.Hasta));
                if (grupo is null)
                {
                    grupo = new(direccion.Key, envio.Punto?.Direccion ?? envio.Numero);
                    grupos.Add(grupo);
                }
                grupo.Envios.Add(envio);
                if (envio.Franja is { } franja)
                {
                    grupo.Desde = grupo.Desde is { } desde ? Max(desde, franja.Desde) : franja.Desde;
                    grupo.Hasta = grupo.Hasta is { } hasta ? Min(hasta, franja.Hasta) : franja.Hasta;
                }
            }
            resultado.AddRange(grupos);
        }
        return resultado;
    }
    private static TimeOnly Max(TimeOnly a, TimeOnly b) => a > b ? a : b;
    private static TimeOnly Min(TimeOnly a, TimeOnly b) => a < b ? a : b;
}

internal sealed class GrupoParada(string clave, string direccion)
{
    public string Clave { get; } = clave;
    public string Direccion { get; } = direccion;
    public TimeOnly? Desde { get; set; }
    public TimeOnly? Hasta { get; set; }
    public List<EnvioRuta> Envios { get; } = [];
}

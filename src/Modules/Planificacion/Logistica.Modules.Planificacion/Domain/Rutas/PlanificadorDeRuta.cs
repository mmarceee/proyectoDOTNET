using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal static class PlanificadorDeRuta
{
    public static void ExigirFechaVigente(DateOnly fecha, DateOnly hoy)
    {
        if (fecha < hoy) throw new DomainException("La fecha de la ruta no puede ser anterior a hoy.");
    }

    public static EvaluacionRuta Evaluar(DateOnly fecha, VehiculoRuta vehiculo, int maxParadas, IReadOnlyList<EnvioRuta> envios)
    {
        var errores = new List<RestriccionRuta>();
        if (envios.Select(e => e.Id).Distinct().Count() != envios.Count)
            errores.Add(new("EnvioRepetido", "Un envío no puede aparecer dos veces en la selección."));
        if (envios.Count > maxParadas)
            errores.Add(new("MaximoParadasExcedido", $"La ruta admite hasta {maxParadas} paradas."));
        decimal peso = 0, volumen = 0;
        foreach (var envio in envios)
        {
            if (envio.Fecha is DateOnly programada && programada != fecha)
                errores.Add(new("FechaIncompatible", $"El envío {envio.Numero} está programado para {programada:dd/MM/yyyy}.", envio.Id));
            if (envio.FranjaRequerida && (envio.Franja is null || !envio.Franja.Dias.Contains(fecha.DayOfWeek)
                || (envio.ZonaId is Guid zona && envio.Franja.ZonaId != zona)))
                errores.Add(new("FranjaIncompatible", $"La franja de {envio.Numero} no se ofrece para el día de la ruta.", envio.Id));
            if (envio.Bultos.Count == 0)
                errores.Add(new("EnvioSinCarga", $"El envío {envio.Numero} no tiene bultos.", envio.Id));
            foreach (var bulto in envio.Bultos)
            {
                if (bulto.PesoKg <= 0 || bulto.LargoCm <= 0 || bulto.AnchoCm <= 0 || bulto.AltoCm <= 0)
                {
                    errores.Add(new("MedidasInvalidas", "El peso y las dimensiones deben ser mayores a cero.", envio.Id, bulto.BultoId));
                    continue;
                }
                peso += bulto.PesoKg;
                volumen += bulto.VolumenM3;
                var medidas = new[] { bulto.LargoCm, bulto.AnchoCm, bulto.AltoCm }.Order().ToArray();
                var caja = new[] { vehiculo.LargoCm, vehiculo.AnchoCm, vehiculo.AltoCm }.Order().ToArray();
                if (Enumerable.Range(0, 3).Any(i => medidas[i] > caja[i]))
                    errores.Add(new("BultoNoAdmitido", $"Un bulto de {envio.Numero} no entra en el vehículo, incluso rotándolo.", envio.Id, bulto.BultoId));
            }
        }
        if (peso > vehiculo.PesoKg) errores.Add(new("CapacidadPesoExcedida", "Se supera la capacidad de peso del vehículo."));
        if (volumen > vehiculo.VolumenM3) errores.Add(new("CapacidadVolumenExcedida", "Se supera la capacidad de volumen del vehículo."));
        return new(peso, volumen, envios.Count, errores);
    }
}

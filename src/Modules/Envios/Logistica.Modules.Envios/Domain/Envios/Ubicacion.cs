using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

internal sealed record Ubicacion
{
    public decimal Latitud { get; private init; }
    public decimal Longitud { get; private init; }

    public Ubicacion(decimal latitud, decimal longitud)
    {
        if (latitud is < -90 or > 90 || longitud is < -180 or > 180)
        {
            throw new DomainException("La ubicación tiene coordenadas inválidas.");
        }

        Latitud = latitud;
        Longitud = longitud;
    }
}

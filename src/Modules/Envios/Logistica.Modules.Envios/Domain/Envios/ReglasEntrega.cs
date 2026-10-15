using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

// Lo que la versión de reglas del envío exige al entregarlo o al registrar un intento fallido (letra, sección 6.6;
// modelo de dominio, VersionReglas y PruebaExigida). Las reglas viven en Administración: Envíos recibe esta copia
// para decidir. La firma no figura porque no se configura: es siempre obligatoria (la valida IntentoEntrega).
internal sealed record ReglasEntrega
{
    public bool ExigeFoto { get; }
    public bool ExigeNombreYDocumento { get; }
    public int MaxIntentos { get; }
    public TimeSpan PlazoEntreIntentos { get; }

    public ReglasEntrega(bool exigeFoto, bool exigeNombreYDocumento, int maxIntentos, TimeSpan plazoEntreIntentos)
    {
        if (maxIntentos < 1)
        {
            throw new DomainException("El máximo de intentos de entrega debe ser al menos 1.");
        }

        if (plazoEntreIntentos < TimeSpan.Zero)
        {
            throw new DomainException("El plazo entre intentos no puede ser negativo.");
        }

        ExigeFoto = exigeFoto;
        ExigeNombreYDocumento = exigeNombreYDocumento;
        MaxIntentos = maxIntentos;
        PlazoEntreIntentos = plazoEntreIntentos;
    }
}

using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Envios;

// El par estado actual → estado nuevo no está en la tabla de transiciones. Es una regla de negocio
// violada, así que la API responde 400.
internal sealed class TransicionInvalidaException(EstadoEnvio origen, EstadoEnvio destino)
    : DomainException($"El envío no puede pasar de {origen} a {destino}.");

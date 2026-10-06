using Logistica.Modules.Envios.Domain.Envios;

namespace Logistica.Modules.Envios.Application.Features.CrearEnvio;

// Lo que hace falta para crear un envío, ya traducido desde HTTP. El operador y el comercio
// no vienen acá: salen de la sesión (ICurrentTenant), para que nadie cree envíos a nombre de otro.
internal sealed record CrearEnvioCommand(
    Destinatario Destinatario,
    Direccion Direccion,
    IReadOnlyList<BultoACrear> Bultos,
    OrigenEvento Origen);

// Peso y medidas de un bulto. La tarifa no viene del cliente: la calcula el sistema.
internal sealed record BultoACrear(decimal PesoKg, decimal LargoCm, decimal AnchoCm, decimal AltoCm);

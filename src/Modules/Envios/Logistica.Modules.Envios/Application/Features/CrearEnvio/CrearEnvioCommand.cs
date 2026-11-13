using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Administracion.Contracts;

namespace Logistica.Modules.Envios.Application.Features.CrearEnvio;

// Lo que hace falta para crear un envío, ya traducido desde HTTP. El operador y el comercio
// no vienen acá: salen de la sesión (ICurrentTenant), para que nadie cree envíos a nombre de otro.
// La referencia del comercio la usa la importación (CU-11) para no duplicar envíos.
internal sealed record CrearEnvioCommand(
    Destinatario Destinatario,
    Direccion Direccion,
    IReadOnlyList<BultoACrear> Bultos,
    OrigenEvento Origen,
    Modalidad Modalidad = Modalidad.Estandar,
    string? ReferenciaComercio = null,
    bool EsPrueba = false,
    DateOnly? FechaEntrega = null,
    Guid? FranjaId = null);

// Peso y medidas de un bulto. La tarifa no viene del cliente: la calcula el sistema.
internal sealed record BultoACrear(decimal PesoKg, decimal LargoCm, decimal AnchoCm, decimal AltoCm);

// El resultado de un alta con referencia: el envío creado o el que ya existía con esa referencia (RF 7).
internal sealed record AltaEnvio(Envio Envio, bool Creado);

// Lo que costaría el envío, sin crearlo: la vista previa de CU-11 lo muestra antes de confirmar. PlazoComprometido es
// el plazo de la modalidad en la versión de reglas vigente (CU-71); null si la versión no lo define.
internal sealed record CotizacionEnvio(
    Guid ZonaId, Guid VersionReglasId, DatosVersionTarifario? VersionTarifario, IReadOnlyList<DatosBulto> Bultos,
    TimeSpan? PlazoComprometido = null)
{
    public decimal Total => Bultos.Sum(b => b.MontoTarifa);
}

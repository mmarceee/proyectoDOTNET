using Logistica.Modules.Envios.Domain.Envios;

namespace Logistica.Modules.Envios.Application;

// De dónde salen las reglas que exige cada envío al entregarlo o al registrar un intento (CU-17 y CU-18). Las reglas
// viven en Administración (VersionReglas, CU-06). Mientras CU-06 no exista, la implementación es provisoria
// (ReglasDeEntregaProvisorias); después se reemplaza por una que las pida a Administración, sin tocar el dominio.
internal interface IReglasDeEntrega
{
    Task<ReglasEntrega> DelEnvioAsync(Envio envio, CancellationToken ct);

    // null si el motivo no está en el catálogo de la versión de reglas del envío.
    Task<MotivoNoEntrega?> MotivoAsync(Envio envio, Guid motivoId, CancellationToken ct);
}

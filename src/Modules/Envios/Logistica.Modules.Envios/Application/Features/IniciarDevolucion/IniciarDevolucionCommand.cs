using Logistica.Modules.Envios.Domain.Envios;

namespace Logistica.Modules.Envios.Application.Features.IniciarDevolucion;

// Lo que hace falta para iniciar la devolución de un envío, ya traducido desde la página o la API (CU-20). El origen
// dice quién la pide (Backoffice o Portal del comercio), porque el comercio tiene una regla propia (A2). El responsable
// es el usuario de la sesión.
internal sealed record IniciarDevolucionCommand(Guid EnvioId, string Motivo, OrigenEvento Origen, Guid? ResponsableId);

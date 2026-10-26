using Logistica.Http.Contracts.Seguimiento;

namespace Logistica.Modules.Seguimiento.Application.Features.VerTablero;

// CU-66, paso 3: avisa al tablero de un operador que algo cambió. Application no conoce SignalR (ADR-0001): la
// implementación está en Presentation. Siempre se avisa a un operador, nunca a todos (ADR-0002, sección 2.5).
internal interface ITableroEnVivo
{
    Task PosicionAsync(Guid operadorId, PosicionTableroResponse posicion, CancellationToken ct);

    Task NovedadAsync(Guid operadorId, NovedadTableroResponse novedad, CancellationToken ct);
}

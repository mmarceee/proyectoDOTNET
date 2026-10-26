using Logistica.Modules.Administracion.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Logistica.Modules.Seguimiento.Presentation.Features.VerTablero;

// CU-66, requerimiento especial 1: la conexión en tiempo real del tablero, con la misma autenticación (la cookie) y
// los mismos perfiles que la página. Cada conexión entra a la sala de su operador, tomado de la sesión y nunca del
// navegador (ADR-0002, sección 2.5). El navegador sólo escucha: no hay métodos que pueda invocar.
[Authorize(Roles = "Administrador,Despachador")]
internal sealed class TableroHub : Hub
{
    public const string Ruta = "/hubs/tablero";

    public static string Sala(Guid operadorId) => $"operador-{operadorId:N}";

    public override async Task OnConnectedAsync()
    {
        // Sin operador en la sesión no hay sala: se corta la conexión (falla cerrado).
        if (!Guid.TryParse(Context.User?.FindFirst(ClaimsDeSesion.OperadorId)?.Value, out var operadorId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Sala(operadorId));
        await base.OnConnectedAsync();
    }
}

using System.Globalization;
using Logistica.Http.Contracts.Seguimiento;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Application.Features.SolicitarReprogramacion;

// CU-61 desde el seguimiento público. El inquilino ya salió del token (InquilinoDelToken). La reprogramación la hace
// Envíos, dueño de la transición (decisión H2); acá sólo se traduce para la destinataria y, al terminar, se borra su
// seguimiento de la caché para que vea la nueva ventana al instante (ADR-0006, sección 2.5: después de confirmar).
internal sealed class SolicitarReprogramacionHandler(IEnviosModuleApi envios, ICacheDistribuida cache)
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-UY");

    // null si el envío no existe o es de otro operador: el endpoint responde igual que a un enlace inválido.
    public async Task<OpcionesReprogramacionResponse?> OpcionesAsync(Guid envioId, CancellationToken ct)
    {
        if (await envios.OpcionesReprogramacionAsync(envioId, ct) is not { } opciones)
        {
            return null;
        }

        return new OpcionesReprogramacionResponse(opciones.Select(o => new OpcionReprogramacionResponse(o.Fecha, o.FranjaId,
            $"{o.Fecha.ToString("dddd d 'de' MMMM", Cultura)}, de {o.Desde:HH\\:mm} a {o.Hasta:HH\\:mm}")).ToList());
    }

    // false si el envío no existe o es de otro operador. Una regla rota (A1, A2, A3) sale como DomainException (400).
    public async Task<bool> ReprogramarAsync(Guid envioId, SolicitarReprogramacionRequest pedido, CancellationToken ct)
    {
        if (!await envios.ReprogramarPorDestinatarioAsync(envioId, pedido.Fecha, pedido.FranjaId, ct))
        {
            return false;
        }

        await cache.InvalidarAsync(ConsultarSeguimientoHandler.Cache, ConsultarSeguimientoHandler.Clave(envioId), ct);
        return true;
    }
}

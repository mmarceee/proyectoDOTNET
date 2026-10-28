using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.SharedKernel;

namespace Logistica.Modules.Deposito.Application.Features.ConfirmarDevolucion;

internal sealed record ConfirmarDevolucionCommand(string NumeroOCodigo, IReadOnlyCollection<Guid> BultosEntregados,
    string? NombreReceptor, string? DocumentoReceptor, bool ConfirmarParcial, Guid? ResponsableId);

internal enum EstadoConfirmacion { Confirmada, FaltanBultos, NoEncontrada }

internal sealed record ConfirmacionDevolucion(EstadoConfirmacion Estado, string? Numero = null);

// CU-31, nivel mínimo: el operario entrega al comercio un envío devuelto. Depósito coordina; la devolución y el envío son
// de Envíos, que cierra una y pasa el otro a Devuelto (T15) por su contrato (ADR-0001, addendum 3).
internal sealed class ConfirmarDevolucionHandler(IEnviosModuleApi envios, IAdministracionModuleApi administracion,
    IUnidadDeTrabajo unidad)
{
    // Pasos 1 y 2: por el número del envío o por el código de uno de sus bultos.
    public Task<DevolucionParaEntrega?> ConsultarAsync(string numeroOCodigo, CancellationToken ct) =>
        envios.ConsultarDevolucionAsync(numeroOCodigo, ct);

    public async Task<ConfirmacionDevolucion> ConfirmarAsync(ConfirmarDevolucionCommand command, CancellationToken ct)
    {
        // Requerimiento especial 1: sin saber quién recibió, no hay con qué dirimir un reclamo.
        if (string.IsNullOrWhiteSpace(command.NombreReceptor) || string.IsNullOrWhiteSpace(command.DocumentoReceptor))
        {
            throw new DomainException("Completá el nombre y el documento de quien recibe por el comercio.");
        }

        var resultado = new ConfirmacionDevolucion(EstadoConfirmacion.NoEncontrada);
        await unidad.EjecutarAsync(async () =>
        {
            // El mismo bloqueo por operador que CU-55: dos operarios no entregan la misma devolución a la vez, ni mientras
            // se confirma la rendición que la trae al depósito.
            await administracion.BloquearPlanificacionAsync(ct);

            if (await envios.ConsultarDevolucionAsync(command.NumeroOCodigo, ct) is not { } devolucion)
            {
                return;
            }

            // A2: entregar sin todos los bultos pide una confirmación antes de registrar la discrepancia.
            if (!command.ConfirmarParcial && devolucion.Bultos.Any(b => !command.BultosEntregados.Contains(b.Id)))
            {
                resultado = new ConfirmacionDevolucion(EstadoConfirmacion.FaltanBultos, devolucion.Numero);
                return;
            }

            await envios.ConfirmarDevolucionAsync(new DevolucionAConfirmar(devolucion.EnvioId, command.NombreReceptor,
                command.DocumentoReceptor, command.BultosEntregados, command.ResponsableId), ct);
            resultado = new ConfirmacionDevolucion(EstadoConfirmacion.Confirmada, devolucion.Numero);
        }, ct);

        return resultado;
    }
}

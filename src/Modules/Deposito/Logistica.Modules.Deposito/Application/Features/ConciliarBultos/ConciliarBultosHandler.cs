using System.Text.Json;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Deposito.Application.Features.ConciliarBultos;

internal sealed class ConciliarBultosHandler(IEnviosModuleApi envios,
    IPlanificacionModuleApi rutas, IAdministracionModuleApi administracion, IOperacionesDeposito operaciones,
    IUnidadDeTrabajo unidad)
{
    public async Task<int> HandleAsync(ConciliacionBultos pedido, CancellationToken ct)
    {
        if (pedido.OperacionId == Guid.Empty || pedido.Bultos is null || pedido.Bultos.Count is < 1 or > 2000
            || pedido.Bultos.Any(b => b.BultoId == Guid.Empty || b.RevisionEsperada < 0)
            || pedido.Bultos.Select(b => b.BultoId).Distinct().Count() != pedido.Bultos.Count
            || string.IsNullOrWhiteSpace(pedido.Motivo) || pedido.Motivo.Trim().Length > 500)
            throw new DomainException("Indicá bultos, revisiones, operación y un motivo de hasta 500 caracteres.");
        var resultado = 0;
        var huella = JsonSerializer.Serialize(new { Tipo = "ConciliarCustodia", Pedido = pedido });
        await unidad.EjecutarAsync(async () =>
        {
            await administracion.BloquearPlanificacionAsync(ct);
            if (await operaciones.ResultadoAsync(pedido.OperacionId, huella, ct) is { } previa)
            { resultado = JsonSerializer.Deserialize<int>(previa); return; }
            var ids = pedido.Bultos.Select(b => b.BultoId).ToArray();
            var activas = await rutas.ConsultarReservasActivasAsync(ids, ct);
            if (pedido.RutaId is Guid ruta)
            {
                if (pedido.RevisionRuta is not long revision || await rutas.RevisionAsync(ruta, ct) != revision
                    || activas.Count != ids.Length || activas.Any(r => r.RutaId != ruta))
                    throw new DomainException("La ruta o sus reservas cambiaron. Recargá antes de confirmar.");
            }
            else if (activas.Count != 0 || pedido.RevisionRuta is not null)
                throw new DomainException("Los bultos reservados se concilian con su ruta y revisión.");

            await envios.ConfirmarCustodiaAsync(pedido, ct);
            if (pedido.RutaId is Guid rutaId)
            {
                if (pedido.EnDeposito) await envios.ReintegrarBultosAsync(rutaId, ids, pedido.ResponsableId, ct);
                await rutas.RegistrarConciliacionAsync(rutaId, pedido.RevisionRuta!.Value, ct);
            }
            resultado = ids.Length;
            await operaciones.RegistrarAsync(pedido.OperacionId, huella, JsonSerializer.Serialize(resultado), ct);
        }, ct);
        return resultado;
    }
}

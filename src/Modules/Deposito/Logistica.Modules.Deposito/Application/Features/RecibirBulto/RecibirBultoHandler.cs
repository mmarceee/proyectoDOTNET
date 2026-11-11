using Logistica.Modules.Deposito.Domain.Recepciones;
using Logistica.Modules.Envios.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Deposito.Application.Features.RecibirBulto;

// CU-30, versión mínima: registra la recepción de un bulto y le pide a Envíos que aplique T2 a ese bulto (RF 31);
// cuando llegaron todos, el envío queda EnDeposito. Depósito nunca toca las tablas de Envíos: usa su contrato.
internal sealed class RecibirBultoHandler(
    IEnviosModuleApi envios,
    IRecepcionRepository recepciones,
    IUnidadDeTrabajo unidadDeTrabajo,
    ICurrentTenant tenant,
    TimeProvider reloj, IResponsableDeposito responsable)
{
    public async Task<RecepcionRegistrada> HandleAsync(RecibirBultoCommand command, CancellationToken ct)
    {
        var operadorId = tenant.OperadorId
            ?? throw new InvalidOperationException("La sesión no tiene un operador.");

        // El lector escribe el código como un teclado: se normaliza igual que los códigos guardados.
        var codigo = command.CodigoBulto.Trim().ToUpperInvariant();

        // A1: el mismo mensaje si el código no existe o es de otro operador, para no revelar nada.
        var envio = await envios.ObtenerParaRecepcionAsync(codigo, ct)
            ?? throw new DomainException("Bulto desconocido.");

        if (envio.Cancelado)
        {
            // A3
            throw new DomainException(
                $"El envío {envio.Numero} fue cancelado: separá el bulto para devolverlo al comercio.");
        }

        if (!envio.PendienteDeRecepcion)
        {
            throw new DomainException($"El envío {envio.Numero} ya no está pendiente de recepción.");
        }

        var bulto = envio.Bultos.Single(b => b.Codigo == codigo);
        var recibidos = (await recepciones.BultosRecibidosAsync(envio.EnvioId, ct)).ToHashSet();

        if (recibidos.Contains(bulto.BultoId))
        {
            // A2
            throw new DomainException($"El bulto {codigo} ya se escaneó.");
        }

        var recepcion = RecepcionDeposito.Registrar(
            operadorId,
            envio.EnvioId,
            bulto.BultoId,
            new Medidas(bulto.PesoKg, bulto.LargoCm, bulto.AnchoCm, bulto.AltoCm),
            command.Medidas,
            reloj.GetUtcNow());

        recepciones.Agregar(recepcion);
        recibidos.Add(bulto.BultoId);

        var envioCompleto = false;

        // La recepción (Depósito) y la transición T2 del bulto (Envíos) se guardan juntas o ninguna
        // (ADR-0001, addendum 3; CU-30, requerimiento especial 3). Con RF 31 cada bulto escaneado cambia de estado.
        await unidadDeTrabajo.EjecutarAsync(async () =>
        {
            await recepciones.GuardarCambiosAsync(ct);
            envioCompleto = await envios.RecibirBultoAsync(envio.EnvioId, bulto.BultoId, responsable.Id, ct, recepcion.Id);
        }, ct);

        return new RecepcionRegistrada(
            envio.Numero,
            recepcion.Resultado,
            recepcion.Discrepancia,
            envio.Bultos.Select(b => new EstadoBulto(b.Codigo, recibidos.Contains(b.BultoId))).ToList(),
            envioCompleto);
    }
}

using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Application;

// Implementación del contrato de Envíos. Es internal: los otros módulos sólo ven la interfaz.
internal sealed class EnviosModuleApi(IEnvioRepository envios, TimeProvider reloj) : IEnviosModuleApi
{
    public async Task<EnvioParaRecepcion?> ObtenerParaRecepcionAsync(string codigoBulto, CancellationToken ct)
    {
        var envio = await envios.ObtenerPorCodigoBultoAsync(codigoBulto, ct);

        if (envio is null)
        {
            return null;
        }

        return new EnvioParaRecepcion(
            envio.Id,
            envio.Numero,
            PendienteDeRecepcion: envio.Estado == EstadoEnvio.Admitido,
            Cancelado: envio.Estado == EstadoEnvio.Cancelado,
            envio.Bultos
                .OrderBy(b => b.Codigo)
                .Select(b => new BultoDeclarado(b.Id, b.Codigo, b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm))
                .ToList());
    }

    public async Task RecibirEnDepositoAsync(Guid envioId, Guid? responsableId, CancellationToken ct)
    {
        var envio = await envios.ObtenerAsync(envioId, ct)
            ?? throw new DomainException("El envío no existe.");

        // T2. Si el envío ya no está Admitido (por ejemplo, lo cancelaron), Transicionar lo rechaza.
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, responsableId, reloj.GetUtcNow());

        await envios.GuardarCambiosAsync(ct);
    }
}

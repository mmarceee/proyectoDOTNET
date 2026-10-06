using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Application.Features.CrearEnvio;

// CU-10, versión mínima del 8/10: un envío del Portal, sin zona, franja ni tarifa.
// Coordina los pasos; las reglas del envío están en Envio.Crear.
internal sealed class CrearEnvioHandler(IEnvioRepository envios, ICurrentTenant tenant, TimeProvider reloj)
{
    // Hasta el 15/10 la tarifa es cero. Después la calcula Administración para cada bulto (CU-10, paso 7).
    private const decimal TarifaProvisoria = 0m;

    public async Task<Envio> HandleAsync(CrearEnvioCommand command, CancellationToken ct)
    {
        var operadorId = tenant.OperadorId
            ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        var comercioId = tenant.ComercioId
            ?? throw new InvalidOperationException("La sesión no tiene un comercio.");

        var bultos = command.Bultos
            .Select(b => new DatosBulto(b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm, TarifaProvisoria))
            .ToList();

        var numero = await envios.SiguienteNumeroAsync(ct);

        var envio = Envio.Crear(
            operadorId,
            comercioId,
            numero,
            command.Destinatario,
            command.Direccion,
            bultos,
            command.Origen,
            responsableId: null,
            reloj.GetUtcNow());

        envios.Agregar(envio);
        await envios.GuardarCambiosAsync(ct);

        return envio;
    }
}

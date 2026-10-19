using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;
using Logistica.Modules.Administracion.Contracts;

namespace Logistica.Modules.Envios.Application.Features.CrearEnvio;

// CU-10, versión mínima del 8/10: un envío del Portal, sin zona, franja ni tarifa.
// Coordina los pasos; las reglas del envío están en Envio.Crear.
internal sealed class CrearEnvioHandler(IEnvioRepository envios, ICurrentTenant tenant, TimeProvider reloj,
    IRelacionComercialActiva relacion, IUnidadDeTrabajo unidad, IAdministracionModuleApi administracion)
{
    // Hasta el 15/10 la tarifa es cero. Después la calcula Administración para cada bulto (CU-10, paso 7).
    private const decimal TarifaProvisoria = 0m;

    public async Task<Envio> HandleAsync(CrearEnvioCommand command, CancellationToken ct)
    {
        var operadorId = tenant.OperadorId
            ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        var comercioId = tenant.ComercioId
            ?? throw new InvalidOperationException("La sesión no tiene un comercio.");

        // El envío guarda la versión de reglas que rige al admitirlo: las entregas y los intentos se juzgan con ella,
        // aunque después se publique otra (letra 6.6). Sin reglas vigentes no se admiten envíos.
        var reglas = await administracion.ReglasDeEntregaVigentesAsync(ct)
            ?? throw new DomainException("El operador no tiene reglas operativas vigentes: todavía no puede admitir envíos.");

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
            reloj.GetUtcNow(),
            versionReglasId: reglas.VersionId);

        await unidad.EjecutarAsync(async () =>
        {
            await relacion.ExigirAsync(comercioId, ct);
            envios.Agregar(envio);
            await envios.GuardarCambiosAsync(ct);
        }, ct);

        return envio;
    }
}

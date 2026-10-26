using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;
using Logistica.Modules.Administracion.Contracts;

namespace Logistica.Modules.Envios.Application.Features.CrearEnvio;

// CU-10: resuelve la cobertura y tarifa cada bulto con el cuadro vigente de Administración.
// La modalidad elegida en el Portal determina las reglas y los ajustes aplicables.
internal sealed class CrearEnvioHandler(IEnvioRepository envios, ICurrentTenant tenant, TimeProvider reloj,
    IRelacionComercialActiva relacion, IUnidadDeTrabajo unidad, IAdministracionModuleApi administracion,
    IIntegrationEventPublisher eventos)
{
    public async Task<Envio> HandleAsync(CrearEnvioCommand command, CancellationToken ct)
    {
        var operadorId = tenant.OperadorId
            ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        var comercioId = tenant.ComercioId
            ?? throw new InvalidOperationException("La sesión no tiene un comercio.");
        if (!Enum.IsDefined(command.Modalidad))
            throw new DomainException("Elegí una modalidad válida: Estándar o Urgente.");

        // El envío guarda la versión de reglas que rige al admitirlo: las entregas y los intentos se juzgan con ella,
        // aunque después se publique otra (letra 6.6). Sin reglas vigentes no se admiten envíos.
        var reglas = await administracion.ReglasDeEntregaVigentesAsync(ct)
            ?? throw new DomainException("El operador no tiene reglas operativas vigentes: todavía no puede admitir envíos.");

        if (command.Bultos.Count == 0)
            throw new DomainException("Un envío debe tener al menos un bulto.");
        if (command.Bultos.Any(b => b.PesoKg <= 0 || b.LargoCm <= 0 || b.AnchoCm <= 0 || b.AltoCm <= 0))
            throw new DomainException("El peso y las medidas del bulto deben ser mayores a cero.");

        var recursos = await administracion.ConsultarRecursosPlanificacionAsync(DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime), ct);
        var zona = recursos.Zonas.SingleOrDefault(z => z.CodigosPostales.Contains(command.Direccion.CodigoPostal))
            ?? throw new DomainException("El operador no cubre el código postal de la dirección.");

        var bultos = new List<DatosBulto>();
        DatosVersionTarifario? versionTarifario = null;
        foreach (var bulto in command.Bultos)
        {
            decimal volumenM3;
            try { volumenM3 = (bulto.LargoCm / 100m) * (bulto.AnchoCm / 100m) * (bulto.AltoCm / 100m); }
            catch (OverflowException) { throw new DomainException($"Las medidas del bulto {bultos.Count + 1} son demasiado grandes."); }

            var tarifa = await administracion.CalcularTarifaAsync(zona.Id, command.Modalidad, bulto.PesoKg, volumenM3, ct)
                ?? throw new DomainException($"El bulto {bultos.Count + 1} no tiene una tarifa aplicable en el cuadro tarifario vigente.");
            if (versionTarifario is not null && versionTarifario.Id != tarifa.VersionTarifarioId)
                throw new DomainException("El cuadro tarifario cambió durante el alta. Volvé a confirmar el envío.");
            versionTarifario = new DatosVersionTarifario(tarifa.VersionTarifarioId, tarifa.VersionTarifarioNumero);
            bultos.Add(new DatosBulto(bulto.PesoKg, bulto.LargoCm, bulto.AnchoCm, bulto.AltoCm, tarifa.Monto));
        }

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
            versionTarifario: versionTarifario,
            versionReglasId: reglas.VersionId,
            modalidad: command.Modalidad);
        envio.DefinirPlanificacion(zona.Id, null, null);

        await unidad.EjecutarAsync(async () =>
        {
            await relacion.ExigirAsync(comercioId, ct);
            envios.Agregar(envio);
            // T1 también llega a Seguimiento: es el único evento con el comercio en el sobre (CU-64).
            eventos.Registrar(EnvioAdmitido.Tipo, new EnvioAdmitido(envio.Id, envio.Numero, envio.CreadoEn,
                envio.Destinatario.Nombre, envio.Destinatario.Email), envio.CreadoEn);
            await envios.GuardarCambiosAsync(ct);
        }, ct);

        return envio;
    }
}

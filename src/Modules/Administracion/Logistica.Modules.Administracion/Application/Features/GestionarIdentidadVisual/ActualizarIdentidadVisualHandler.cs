using Logistica.Modules.Administracion.Contracts.Events;
using Logistica.Modules.Administracion.Domain.Operadores;
using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Application.Features.GestionarIdentidadVisual;

// CU-07: el administrador actualiza la marca de su operador. Coordina; las reglas (A1 y A2) las validan LogoOperador
// e IdentidadVisual en el dominio.
internal sealed class ActualizarIdentidadVisualHandler(IOperadorRepository operadores, ICurrentTenant tenant,
    IIntegrationEventPublisher eventos, TimeProvider reloj)
{
    public async Task<ResultadoIdentidadVisual> HandleAsync(ActualizarIdentidadVisualCommand command, CancellationToken ct)
    {
        var operadorId = tenant.OperadorId
            ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        var operador = await operadores.ObtenerAsync(operadorId, ct)
            ?? throw new InvalidOperationException("El operador de la sesión no existe.");

        var logoId = operador.Identidad.LogoArchivoId;
        if (command.Logo is not null)
        {
            var logo = LogoOperador.Crear(operadorId, command.Logo.TipoContenido, command.Logo.Contenido);
            operadores.AgregarLogo(logo);
            logoId = logo.Id;
        }

        var identidad = new IdentidadVisual(
            command.ColorPrimario, command.ColorSecundario, logoId, command.EmailContacto, command.TelefonoContacto);
        operador.ActualizarIdentidad(identidad);
        // CU-62: Seguimiento guarda una copia para los correos al destinatario, en la misma transacción que el cambio.
        eventos.Registrar(IdentidadVisualActualizada.Tipo, new IdentidadVisualActualizada(operador.Nombre,
            identidad.ColorPrimario, identidad.ColorSecundario, identidad.EmailContacto, identidad.TelefonoContacto),
            reloj.GetUtcNow());

        // El logo y la identidad se guardan juntos. Invalidar la caché de la identidad llega con el ADR de caché (22/10).
        await operadores.GuardarCambiosAsync(ct);

        return new ResultadoIdentidadVisual(ContrasteInsuficiente: !identidad.TextoBlancoLegible());
    }
}

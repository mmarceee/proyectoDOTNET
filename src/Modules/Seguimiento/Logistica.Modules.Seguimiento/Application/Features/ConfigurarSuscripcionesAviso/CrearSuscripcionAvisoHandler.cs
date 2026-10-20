using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Application.Features.ConfigurarSuscripcionesAviso;

internal sealed record CrearSuscripcionAvisoCommand(string UrlDestino, IReadOnlyCollection<TipoEventoAviso> TiposEvento);

internal sealed class CrearSuscripcionAvisoHandler(ISuscripcionesAvisoRepository suscripciones,
    IAdministracionModuleApi administracion, ISecretosAviso secretos, ICurrentTenant tenant, IUnidadDeTrabajo unidad)
{
    public async Task<SecretoSuscripcionAviso> HandleAsync(CrearSuscripcionAvisoCommand command, CancellationToken ct)
    {
        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("La sesión no tiene un operador.");
        var comercioId = tenant.ComercioId ?? throw new InvalidOperationException("La sesión no tiene un comercio.");
        SecretoSuscripcionAviso? resultado = null;
        await unidad.EjecutarAsync(async () =>
        {
            var relacionId = await administracion.ObtenerRelacionComercialActualAsync(ct)
                ?? throw new DomainException("Tu comercio no tiene una relación comercial con el operador seleccionado.");
            var secreto = secretos.Generar();
            var suscripcion = SuscripcionAviso.Crear(operadorId, comercioId, relacionId,
                command.UrlDestino, command.TiposEvento, secretos.Cifrar(secreto, operadorId, comercioId));
            suscripciones.Agregar(suscripcion);
            await suscripciones.GuardarAsync(ct);
            resultado = new(suscripcion.Id, secreto);
        }, ct);
        return resultado!;
    }
}

using Logistica.Modules.Seguimiento.Domain.Notificaciones;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Application.Features.NotificarDestinatario;

internal sealed record IdentidadRecibida(string Nombre, string ColorPrimario, string ColorSecundario, string? EmailContacto,
    string? TelefonoContacto, DateTimeOffset ActualizadaEn);

// Copia la identidad visual del operador del mensaje (CU-07) para los correos de CU-62. Repetir la misma copia no hace
// daño y una anterior no pisa a una más nueva, así que no necesita inbox.
internal sealed class ActualizarIdentidadOperadorHandler(IIdentidadesOperadorRepository identidades, ICurrentTenant tenant)
{
    public async Task HandleAsync(IdentidadRecibida recibida, CancellationToken ct)
    {
        var operadorId = tenant.OperadorId ?? throw new InvalidOperationException("El mensaje no tiene operador.");
        var identidad = await identidades.ObtenerAsync(operadorId, ct);
        if (identidad is null)
        {
            identidades.Agregar(IdentidadOperador.Crear(operadorId, recibida.Nombre, recibida.ColorPrimario,
                recibida.ColorSecundario, recibida.EmailContacto, recibida.TelefonoContacto, recibida.ActualizadaEn));
        }
        else if (!identidad.Actualizar(recibida.Nombre, recibida.ColorPrimario, recibida.ColorSecundario,
                     recibida.EmailContacto, recibida.TelefonoContacto, recibida.ActualizadaEn))
        {
            return;
        }
        await identidades.GuardarAsync(ct);
    }
}

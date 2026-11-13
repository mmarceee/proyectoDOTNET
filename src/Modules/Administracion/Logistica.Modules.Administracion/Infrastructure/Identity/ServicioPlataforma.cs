using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Administracion.Application.Features.GestionarOperadores;
using Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain.Operadores;
using Logistica.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.Modules.Administracion.Infrastructure.Identity;

// El trabajo del administrador general dentro de un operador. Abre un ámbito nuevo y fija el operador con
// InquilinoTrabajo antes de resolver los DbContext (ADR-0002, sección 2.4), como CierreDiarioPeriodico: así el outbox
// del alta registra la invitación con el operador correcto, y la unidad de trabajo del ámbito confirma todo junto.
internal sealed class ServicioPlataforma(IServiceScopeFactory ambitos) : IServicioPlataforma
{
    public Task<Invitacion> CrearOperadorAsync(Operador operador, string nombreAdministrador, string emailAdministrador,
        CancellationToken ct) =>
        EnElOperadorAsync(operador.Id, async servicios =>
        {
            var repositorio = servicios.GetRequiredService<IOperadorRepository>();
            Invitacion? invitacion = null;

            // Todo o nada: si el alta del administrador falla, tampoco queda el operador.
            await servicios.GetRequiredService<IUnidadDeTrabajo>().EjecutarAsync(async () =>
            {
                repositorio.Agregar(operador);
                await repositorio.GuardarCambiosAsync(ct);
                invitacion = await InvitarAsync(servicios, operador.Id, nombreAdministrador, emailAdministrador, ct);
            }, ct);

            return invitacion!;
        });

    public Task<Invitacion> InvitarAdministradorAsync(Guid operadorId, string nombre, string email, CancellationToken ct) =>
        EnElOperadorAsync(operadorId, servicios => InvitarAsync(servicios, operadorId, nombre, email, ct));

    private static Task<Invitacion> InvitarAsync(IServiceProvider servicios, Guid operadorId, string nombre, string email,
        CancellationToken ct) =>
        servicios.GetRequiredService<IServicioUsuarios>().CrearAsync(
            new NuevoUsuario(operadorId, null, nombre, email, Perfil.Administrador, null), ct);

    private async Task<T> EnElOperadorAsync<T>(Guid operadorId, Func<IServiceProvider, Task<T>> trabajo)
    {
        await using var ambito = ambitos.CreateAsyncScope();
        ambito.ServiceProvider.GetRequiredService<InquilinoTrabajo>().Fijar(operadorId);
        return await trabajo(ambito.ServiceProvider);
    }
}

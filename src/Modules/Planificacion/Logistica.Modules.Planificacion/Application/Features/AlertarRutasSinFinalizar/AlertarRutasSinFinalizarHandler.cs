using Logistica.Modules.Planificacion.Application.Abstractions;
using Logistica.Modules.Planificacion.Contracts.Events;
using Logistica.Modules.Planificacion.Domain.Rutas;

namespace Logistica.Modules.Planificacion.Application.Features.AlertarRutasSinFinalizar;

// CU-70, paso 3: las rutas con fecha hasta "hoy" que no llegaron a Finalizada (planificadas sin despachar, despachadas o
// en curso) generan una alerta para el despachador, por Outbox. Sin rutas pendientes no publica nada.
internal sealed class AlertarRutasSinFinalizarHandler(IRutaRepository rutas, IIntegrationEventPublisher eventos,
    TimeProvider reloj)
{
    private static readonly EstadoRuta[] SinFinalizar = [EstadoRuta.Planificada, EstadoRuta.Despachada, EstadoRuta.EnCurso];

    public async Task<int> HandleAsync(DateOnly hoy, CancellationToken ct)
    {
        var pendientes = new List<Guid>();
        foreach (var estado in SinFinalizar)
            pendientes.AddRange((await rutas.ListarAsync(null, estado, ct)).Where(r => r.Fecha <= hoy).Select(r => r.Id));
        if (pendientes.Count == 0) return 0;

        eventos.Registrar(RutasSinFinalizar.Tipo, new RutasSinFinalizar(hoy, pendientes), reloj.GetUtcNow());
        await rutas.GuardarAsync(ct);
        return pendientes.Count;
    }
}

namespace Logistica.Modules.Envios.Application.Features.GestionarIncidencias;

// Lectura del listado (CU-22, A1). La implementa Infrastructure, así Application no depende de EF Core.
internal interface IIncidenciasListadoReader
{
    Task<ConsultarIncidenciasResult> ConsultarAsync(ConsultarIncidenciasQuery query, CancellationToken ct);
}

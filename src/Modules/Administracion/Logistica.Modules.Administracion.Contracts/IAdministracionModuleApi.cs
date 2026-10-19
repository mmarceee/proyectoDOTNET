using Logistica.Modules.Administracion.Contracts.Results;

namespace Logistica.Modules.Administracion.Contracts;

public interface IAdministracionModuleApi
{
    Task BloquearPlanificacionAsync(CancellationToken ct);
    Task<ContextoPlanificacion> ObtenerContextoPlanificacionAsync(CancellationToken ct);
    Task<RecursosPlanificacion> ConsultarRecursosPlanificacionAsync(DateOnly fecha, CancellationToken ct);
    Task<ReglasPlanificacion> ObtenerReglasPlanificacionAsync(DateTimeOffset ahora, CancellationToken ct);
    Task<IReadOnlyList<FranjaPlanificacion>> ResolverFranjasAsync(IReadOnlyCollection<Guid> ids, DateOnly fecha, CancellationToken ct);

    // CU-05 para CU-10 (paso 7): el precio de un bulto con la versión del cuadro tarifario que rige ahora, leída de la
    // caché (ADR-0006). null si no hay versión vigente o ninguna regla cubre el bulto (CU-10, A3).
    Task<TarifaBulto?> CalcularTarifaAsync(Guid zonaId, Modalidad modalidad, decimal pesoKg, decimal volumenM3,
        CancellationToken ct);

    // CU-06: las reglas de una versión, con su catálogo de motivos. null si la versión no existe o es de otro operador.
    Task<ReglasOperativas?> ReglasDeEntregaAsync(Guid versionReglasId, CancellationToken ct);

    // CU-06 para el alta (CU-10): la versión que rige ahora, para guardar su VersionId en el envío.
    // También para los envíos viejos sin versión. null si el operador no tiene reglas vigentes.
    Task<ReglasOperativas?> ReglasDeEntregaVigentesAsync(CancellationToken ct);
}

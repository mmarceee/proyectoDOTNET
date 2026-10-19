namespace Logistica.Modules.Administracion.Domain.Reglas;

// Acceso a las versiones de las reglas operativas (CU-06). Lo implementa Infrastructure, así Application no depende de
// EF Core. No recibe el operador: el filtro "Tenant" ya devuelve sólo las del operador de la sesión (ADR-0002).
// Las versiones se devuelven con sus motivos, salvo en el listado. Es el mismo contrato que el del tarifario.
internal interface IReglasRepository
{
    // Sin motivos: sólo para mostrar las versiones.
    Task<IReadOnlyList<VersionReglas>> ListarAsync(CancellationToken ct);

    // A3 (borrador existente): hay a lo sumo un borrador por operador.
    Task<VersionReglas?> ObtenerBorradorAsync(CancellationToken ct);

    // La última publicada (vigente o programada): se copia al crear un borrador y se cierra al publicar otra.
    Task<VersionReglas?> UltimaPublicadaAsync(CancellationToken ct);

    Task<int> SiguienteNumeroAsync(CancellationToken ct);

    // La que rige en ese momento, para Envíos, Planificación y el seguimiento.
    Task<VersionReglas?> VigenteEnAsync(DateTimeOffset momento, CancellationToken ct);

    // Cuándo empieza la próxima versión programada después de ese momento, si hay: la caché no dura más allá (ADR-0006).
    Task<DateTimeOffset?> ProximoInicioAsync(DateTimeOffset momento, CancellationToken ct);

    Task<VersionReglas?> ObtenerPorNumeroAsync(int numero, CancellationToken ct);

    // Una versión publicada por su Id: la que guardó el envío al admitirse (letra 6.6).
    Task<VersionReglas?> ObtenerAsync(Guid versionId, CancellationToken ct);

    void Agregar(VersionReglas version);

    Task GuardarCambiosAsync(CancellationToken ct);
}

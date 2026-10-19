namespace Logistica.Modules.Administracion.Domain.Tarifas;

// Acceso a las versiones del cuadro tarifario (CU-05). Lo implementa Infrastructure, así Application no depende de
// EF Core. No recibe el operador: el filtro "Tenant" ya devuelve sólo las del operador de la sesión (ADR-0002).
// Las versiones se devuelven con sus reglas y sus ajustes, salvo en el listado.
internal interface ITarifarioRepository
{
    // Sin reglas ni ajustes: sólo para mostrar las versiones.
    Task<IReadOnlyList<VersionTarifario>> ListarAsync(CancellationToken ct);

    // A4: hay a lo sumo un borrador por operador.
    Task<VersionTarifario?> ObtenerBorradorAsync(CancellationToken ct);

    // La última publicada (vigente o programada): se copia al crear un borrador y se cierra al publicar otra.
    Task<VersionTarifario?> UltimaPublicadaAsync(CancellationToken ct);

    Task<int> SiguienteNumeroAsync(CancellationToken ct);

    // La que rige en ese momento, para calcular precios (CU-10).
    Task<VersionTarifario?> VigenteEnAsync(DateTimeOffset momento, CancellationToken ct);

    // Cuándo empieza la próxima versión programada después de ese momento, si hay: la caché no dura más allá (ADR-0006).
    Task<DateTimeOffset?> ProximoInicioAsync(DateTimeOffset momento, CancellationToken ct);

    Task<VersionTarifario?> ObtenerPorNumeroAsync(int numero, CancellationToken ct);

    void Agregar(VersionTarifario version);

    Task GuardarCambiosAsync(CancellationToken ct);
}

namespace Logistica.Modules.Envios.Domain.Envios;

// Acceso a los envíos guardados. Lo implementa Infrastructure, así Application no depende de EF Core.
// Sólo devuelve envíos del inquilino actual: los de otro operador son como si no existieran.
internal interface IEnvioRepository
{
    Task<string> SiguienteNumeroAsync(CancellationToken ct);

    // Con sus bultos. El código del bulto es único dentro del operador.
    Task<Envio?> ObtenerPorCodigoBultoAsync(string codigoBulto, CancellationToken ct);

    Task<Envio?> ObtenerAsync(Guid id, CancellationToken ct);

    void Agregar(Envio envio);
    Task<IReadOnlyList<Envio>> ObtenerParaPlanificacionAsync(IReadOnlyCollection<Guid>? envioIds, CancellationToken ct);
// Los metadatos del sobre se toman de la sesión al registrar el evento.
    void RegistrarAsignacion(Guid envioId, Guid rutaId, Guid? responsableId, DateTimeOffset ahora);
    void RegistrarDesasignacion(Guid envioId, Guid rutaId, Guid? responsableId, DateTimeOffset ahora);

    // CU-16: deja EnvioCancelado en el Outbox; se guarda junto con el cambio de estado en GuardarCambiosAsync.
    void RegistrarCancelacion(Envio envio, string? motivo, DateTimeOffset ahora);

    Task GuardarCambiosAsync(CancellationToken ct);
}

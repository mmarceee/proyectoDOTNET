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

    // Deja el evento de la asignación en el outbox; se guarda con el próximo GuardarCambiosAsync, junto con el envío.
    void RegistrarAsignacion(Guid envioId, Guid rutaId, Guid? responsableId, DateTimeOffset ahora);

    Task GuardarCambiosAsync(CancellationToken ct);
}

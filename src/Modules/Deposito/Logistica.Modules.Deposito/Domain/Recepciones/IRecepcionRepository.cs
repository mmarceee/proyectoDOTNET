namespace Logistica.Modules.Deposito.Domain.Recepciones;

// Acceso a las recepciones guardadas, sólo las del inquilino actual.
internal interface IRecepcionRepository
{
    Task<IReadOnlyList<Guid>> BultosRecibidosAsync(Guid envioId, CancellationToken ct);

    void Agregar(RecepcionDeposito recepcion);

    Task GuardarCambiosAsync(CancellationToken ct);
}

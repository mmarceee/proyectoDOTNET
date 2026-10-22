namespace Logistica.Modules.Seguimiento.Domain.Avisos;

internal interface IEntregasAvisoRepository
{
    void Agregar(EntregaAviso entrega);
    Task<bool> ExisteAsync(Guid eventoId, Guid suscripcionAvisoId, CancellationToken ct);
    Task<EntregaAviso?> ObtenerAsync(Guid id, CancellationToken ct);
    Task GuardarAsync(CancellationToken ct);
}

internal interface IEnviosSeguidosRepository
{
    void Agregar(EnvioSeguido envio);
    Task<EnvioSeguido?> ObtenerAsync(Guid envioId, CancellationToken ct);
}

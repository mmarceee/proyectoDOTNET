namespace Logistica.Modules.Envios.Domain.Envios;

// Acceso a los envíos guardados. Lo implementa Infrastructure, así Application no depende de EF Core.
internal interface IEnvioRepository
{
    Task<string> SiguienteNumeroAsync(CancellationToken ct);

    void Agregar(Envio envio);

    Task GuardarCambiosAsync(CancellationToken ct);
}

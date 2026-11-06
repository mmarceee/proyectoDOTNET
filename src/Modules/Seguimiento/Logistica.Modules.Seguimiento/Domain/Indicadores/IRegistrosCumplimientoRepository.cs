namespace Logistica.Modules.Seguimiento.Domain.Indicadores;

internal interface IRegistrosCumplimientoRepository
{
    void Agregar(RegistroCumplimiento registro);
    Task GuardarAsync(CancellationToken ct);
}

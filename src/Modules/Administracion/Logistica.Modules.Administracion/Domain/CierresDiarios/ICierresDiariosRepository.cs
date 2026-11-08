namespace Logistica.Modules.Administracion.Domain.CierresDiarios;

// Sólo ve los cierres del operador del ámbito (filtro "Tenant", ADR-0002).
internal interface ICierresDiariosRepository
{
    Task<bool> ExisteAsync(DateOnly fecha, CancellationToken ct);
    void Agregar(CierreDiario cierre);
    Task GuardarAsync(CancellationToken ct);
}

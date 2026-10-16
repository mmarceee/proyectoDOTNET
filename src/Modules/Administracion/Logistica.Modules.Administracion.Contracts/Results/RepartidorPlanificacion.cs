namespace Logistica.Modules.Administracion.Contracts.Results;

public sealed record RepartidorPlanificacion(Guid Id, string Nombre, bool Activo,
    bool TieneUsuarioVinculado = false, bool PuedeAccederPwa = false);

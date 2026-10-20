using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Application.Features.ConfigurarSuscripcionesAviso;

internal sealed record SuscripcionAvisoVista(Guid Id, string UrlDestino,
    IReadOnlyList<TipoEventoAviso> TiposEvento, bool Activa);

internal interface ISuscripcionesAvisoReader
{
    Task<IReadOnlyList<SuscripcionAvisoVista>> ListarAsync(CancellationToken ct);
    Task<SuscripcionAvisoVista?> ObtenerAsync(Guid id, CancellationToken ct);
}

internal sealed class ConsultarSuscripcionesAvisoHandler(ISuscripcionesAvisoReader lector, ICurrentTenant tenant)
{
    public Task<IReadOnlyList<SuscripcionAvisoVista>> ListarAsync(CancellationToken ct)
    {
        ExigirComercio();
        return lector.ListarAsync(ct);
    }

    public Task<SuscripcionAvisoVista?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        ExigirComercio();
        return lector.ObtenerAsync(id, ct);
    }

    private void ExigirComercio()
    {
        if (tenant.OperadorId is null || tenant.ComercioId is null)
            throw new InvalidOperationException("La consulta requiere un comercio y un operador en la sesión.");
    }
}

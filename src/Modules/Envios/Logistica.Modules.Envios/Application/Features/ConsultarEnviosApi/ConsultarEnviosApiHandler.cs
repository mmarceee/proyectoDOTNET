using Logistica.Http.Contracts.Envios;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Application.Features.ConsultarEnviosApi;

internal sealed record FiltrosEnviosApi(EstadoResumenApi? Estado, DateOnly? FechaDesde, DateOnly? FechaHasta,
    string? ReferenciaComercio, int Pagina, int TamanoPagina);

internal interface IEnviosApiReader
{
    Task<PaginaEnviosApiResponse> ListarAsync(FiltrosEnviosApi filtros, bool esPrueba, CancellationToken ct);
    Task<DetalleEnvioApiResponse?> ObtenerAsync(string numero, bool esPrueba, CancellationToken ct);
}

internal sealed class ConsultarEnviosApiHandler(IEnviosApiReader reader)
{
    public Task<PaginaEnviosApiResponse> ListarAsync(string? estado, DateOnly? desde, DateOnly? hasta, string? referencia,
        int pagina, int tamanoPagina, bool esPrueba, CancellationToken ct)
    {
        if (pagina < 1)
            throw new DomainException("La página debe ser mayor que cero.");
        if (tamanoPagina is < 1 or > 100)
            throw new DomainException("El tamaño de página debe estar entre 1 y 100.");
        if (desde > hasta)
            throw new DomainException("La fecha desde no puede ser posterior a la fecha hasta.");

        EstadoResumenApi? resumen = null;
        if (!string.IsNullOrWhiteSpace(estado))
        {
            var nombre = estado.Trim();
            if (!Enum.TryParse<EstadoResumenApi>(nombre, ignoreCase: true, out var valor)
                || !string.Equals(nombre, valor.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new DomainException("El estado debe ser Creado, EnOperacion, EntregadoParcialmente o Entregado.");
            resumen = valor;
        }

        return reader.ListarAsync(new(resumen, desde, hasta, Envio.NormalizarReferencia(referencia), pagina, tamanoPagina),
            esPrueba, ct);
    }

    public Task<DetalleEnvioApiResponse?> ObtenerAsync(string numero, bool esPrueba, CancellationToken ct) =>
        reader.ObtenerAsync(numero, esPrueba, ct);
}

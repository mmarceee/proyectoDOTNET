using Logistica.Modules.Envios.Contracts;

namespace Logistica.Modules.Deposito.Application.Features.RecibirBulto;

internal sealed record ConsultarBultoParaRecepcionQuery(string CodigoBulto);
internal sealed record BultoParaRecepcionDto(string CodigoBulto, string NumeroEnvio);

internal sealed class ConsultarBultoParaRecepcionHandler(IEnviosModuleApi envios)
{
    public async Task<BultoParaRecepcionDto?> HandleAsync(ConsultarBultoParaRecepcionQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.CodigoBulto)) return null;
        var codigo = query.CodigoBulto.Trim().ToUpperInvariant();
        var envio = await envios.ObtenerParaRecepcionAsync(codigo, ct);
        return envio is null ? null : new(codigo, envio.Numero);
    }
}

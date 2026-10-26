using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Deposito.Domain.Recepciones;

namespace Logistica.Modules.Deposito.Application.Features.RecibirBulto;

internal sealed record ConsultarBultoParaRecepcionQuery(string CodigoBulto);
internal sealed record BultoParaRecepcionDto(string CodigoBulto, string NumeroEnvio, Medidas Declaradas);

internal sealed class ConsultarBultoParaRecepcionHandler(IEnviosModuleApi envios)
{
    public async Task<BultoParaRecepcionDto?> HandleAsync(ConsultarBultoParaRecepcionQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.CodigoBulto)) return null;
        var codigo = query.CodigoBulto.Trim().ToUpperInvariant();
        var envio = await envios.ObtenerParaRecepcionAsync(codigo, ct);
        if (envio is null) return null;
        var bulto = envio.Bultos.Single(b => b.Codigo == codigo);
        return new(codigo, envio.Numero, new(bulto.PesoKg, bulto.LargoCm, bulto.AnchoCm, bulto.AltoCm));
    }
}

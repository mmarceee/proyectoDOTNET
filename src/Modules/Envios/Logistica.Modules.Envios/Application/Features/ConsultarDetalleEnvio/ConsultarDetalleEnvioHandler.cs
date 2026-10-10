using Logistica.Modules.Deposito.Contracts;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;

internal sealed class ConsultarDetalleEnvioHandler(IEnvioDetalleReader reader, IDepositoModuleApi deposito, ICurrentTenant tenant)
{
    public async Task<EnvioDetalleDto?> HandleAsync(
        ConsultarDetalleEnvioQuery query,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.Numero))
        {
            throw new ArgumentException(
                "El número del envío es obligatorio.",
                nameof(query.Numero));
        }

        var detalle = await reader.ConsultarAsync(query with { Numero = query.Numero.Trim() }, ct);
        if (detalle is null)
        {
            return null;
        }

        // Sólo se consultan recepciones de bultos del envío que ya pasó el filtro de visibilidad.
        var recibidos = (await deposito.BultosRecibidosAsync(detalle.Bultos.Select(b => b.Id).ToArray(), ct)).ToHashSet();
        var permiteTransicion = Enum.TryParse<EstadoEnvio>(detalle.Estado, out var estado)
            && TablaTransiciones.Permite(estado, EstadoEnvio.EnDeposito);
        // Contexto de personal del operador. Identity deberá agregar aquí el permiso de operario;
        // este control no pretende reemplazar la autorización por perfiles todavía pendiente.
        var personalOperador = tenant.OperadorId is not null && tenant.ComercioId is null;
        return detalle with
        {
            Bultos = detalle.Bultos.Select(b => b with
            {
                Recibido = recibidos.Contains(b.Id),
                PuedeRecepcionar = personalOperador && permiteTransicion && !recibidos.Contains(b.Id)
                    && estado == EstadoEnvio.Admitido, // CU-30 sólo admite la recepción inicial, no un reintegro T8.
            }).ToList(),
        };
    }
}

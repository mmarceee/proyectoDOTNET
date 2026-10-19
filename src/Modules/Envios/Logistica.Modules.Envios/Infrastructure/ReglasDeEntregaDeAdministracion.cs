using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.Modules.Envios.Application;
using Logistica.Modules.Envios.Domain.Envios;

namespace Logistica.Modules.Envios.Infrastructure;

// Las reglas de entrega del envío salen de Administración (CU-06), por su contrato (ADR-0001, addendum 3):
// - un envío con VersionReglasId usa esa versión, aunque después se publique otra (letra 6.6, escenario 5);
// - un envío admitido antes de guardar la versión usa la vigente.
// Si la versión del envío no existe, falla: es un dato roto, y aplicar otras reglas lo ocultaría.
internal sealed class ReglasDeEntregaDeAdministracion(IAdministracionModuleApi administracion) : IReglasDeEntrega
{
    // Una hoja de ruta pide las reglas de cada uno de sus envíos: dentro del request, cada versión se pide una vez.
    private readonly Dictionary<Guid, ReglasOperativas> _porVersion = [];
    private ReglasOperativas? _vigentes;

    public async Task<Contracts.ReglasParaHoja> ParaHojaAsync(Envio envio, CancellationToken ct)
    {
        var reglas = await DeAdministracionAsync(envio, ct);
        return new Contracts.ReglasParaHoja(envio.VersionReglasId is null ? "Vigente" : "DelEnvio", reglas.VersionId,
            reglas.ExigeFoto, reglas.ExigeNombreYDocumento, reglas.MaxIntentos, reglas.PlazoEntreIntentos,
            reglas.Motivos.Select(m => new Contracts.MotivoParaHoja(m.Id, m.Codigo, m.Descripcion, m.Reprogramable,
                m.ExigeEvidencia)).ToArray());
    }

    public async Task<ReglasEntrega> DelEnvioAsync(Envio envio, CancellationToken ct)
    {
        var reglas = await DeAdministracionAsync(envio, ct);
        return new ReglasEntrega(reglas.ExigeFoto, reglas.ExigeNombreYDocumento, reglas.MaxIntentos,
            reglas.PlazoEntreIntentos, reglas.DevolverTrasIntentosAgotados);
    }

    // El motivo se busca en el catálogo de la versión del envío: los Id son de esa versión.
    public async Task<MotivoNoEntrega?> MotivoAsync(Envio envio, Guid motivoId, CancellationToken ct)
    {
        var reglas = await DeAdministracionAsync(envio, ct);
        return reglas.Motivos.SingleOrDefault(m => m.Id == motivoId) is { } motivo
            ? new MotivoNoEntrega(motivo.Id, motivo.Codigo, motivo.Reprogramable, motivo.ExigeEvidencia)
            : null;
    }

    private async Task<ReglasOperativas> DeAdministracionAsync(Envio envio, CancellationToken ct)
    {
        if (envio.VersionReglasId is not Guid versionId)
        {
            return _vigentes ??= await administracion.ReglasDeEntregaVigentesAsync(ct)
                ?? throw new InvalidOperationException("El operador no tiene reglas operativas vigentes.");
        }

        if (!_porVersion.TryGetValue(versionId, out var reglas))
        {
            reglas = await administracion.ReglasDeEntregaAsync(versionId, ct)
                ?? throw new InvalidOperationException(
                    $"No se encontró la versión de reglas {versionId} con que se admitió el envío {envio.Numero}.");
            _porVersion[versionId] = reglas;
        }

        return reglas;
    }
}

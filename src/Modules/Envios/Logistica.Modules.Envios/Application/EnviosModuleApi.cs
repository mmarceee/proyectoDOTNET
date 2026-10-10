using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Application;

// Implementación del contrato de Envíos. Es internal: los otros módulos sólo ven la interfaz.
internal sealed class EnviosModuleApi(IEnvioRepository envios, TimeProvider reloj) : IEnviosModuleApi
{
    public async Task<IReadOnlyList<EnvioPlanificable>> ObtenerParaPlanificacionAsync(IReadOnlyCollection<Guid>? envioIds, CancellationToken ct)
    {
        var lista = await envios.ObtenerParaPlanificacionAsync(envioIds, ct);
        return lista.Select(e => new EnvioPlanificable(e.Id, e.OperadorId, e.Numero, e.Estado.ToString(), e.ZonaId,
            e.FranjaHorariaId, e.FechaEntregaProgramada, $"{e.Direccion.Calle} {e.Direccion.Numero}, {e.Direccion.Localidad}",
            e.Direccion.CodigoPostal, e.Bultos.Select(b => new BultoPlanificable(b.Id, b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm)).ToList())).ToList();
    }
    public async Task AsignarARutaAsync(Guid rutaId, IReadOnlyList<AsignacionEnvioRuta> asignaciones, Guid? responsableId, CancellationToken ct)
    {
        var ids = asignaciones.Select(a => a.EnvioId).ToArray();
        var encontrados = await envios.ObtenerParaPlanificacionAsync(ids, ct);
        if (ids.Distinct().Count() != ids.Length || encontrados.Count != ids.Length)
            throw new DomainException("Uno o más envíos no están disponibles.");
        var ahora = reloj.GetUtcNow();
        foreach (var envio in encontrados)
        {
            var asignacion = asignaciones.Single(a => a.EnvioId == envio.Id);
            envio.DefinirPlanificacion(envio.ZonaId, asignacion.FranjaId, envio.FechaEntregaProgramada);
            envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, responsableId, ahora,
                detalle: $"Asignado a la ruta {rutaId}.");
            envios.RegistrarAsignacion(envio.OperadorId, envio.Id, rutaId, responsableId, ahora);
        }
        await envios.GuardarCambiosAsync(ct);
    }
    public async Task<EnvioParaRecepcion?> ObtenerParaRecepcionAsync(string codigoBulto, CancellationToken ct)
    {
        var envio = await envios.ObtenerPorCodigoBultoAsync(codigoBulto, ct);

        if (envio is null)
        {
            return null;
        }

        return new EnvioParaRecepcion(
            envio.Id,
            envio.Numero,
            PendienteDeRecepcion: envio.Estado == EstadoEnvio.Admitido,
            Cancelado: envio.Estado == EstadoEnvio.Cancelado,
            envio.Bultos
                .OrderBy(b => b.Codigo)
                .Select(b => new BultoDeclarado(b.Id, b.Codigo, b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm))
                .ToList());
    }

    public async Task RecibirEnDepositoAsync(Guid envioId, Guid? responsableId, CancellationToken ct)
    {
        var envio = await envios.ObtenerAsync(envioId, ct)
            ?? throw new DomainException("El envío no existe.");

        // T2. Si el envío ya no está Admitido (por ejemplo, lo cancelaron), Transicionar lo rechaza.
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, responsableId, reloj.GetUtcNow());

        await envios.GuardarCambiosAsync(ct);
    }
}

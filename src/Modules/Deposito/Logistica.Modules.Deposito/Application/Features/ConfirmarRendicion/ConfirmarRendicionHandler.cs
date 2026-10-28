using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Ejecucion.Contracts;
using Logistica.Modules.Ejecucion.Contracts.Results;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.SharedKernel;

namespace Logistica.Modules.Deposito.Application.Features.ConfirmarRendicion;

// Una línea de la rendición con el código de su bulto y si el operario lo escaneó.
internal sealed record LineaComparada(LineaRendicionPendiente Linea, string Codigo, bool Escaneado)
{
    public bool Faltante => Linea.DebeVolver && !Escaneado;
}

// Lo declarado por el repartidor (CU-54) frente a lo escaneado (CU-55, paso 4). Sobrantes: códigos escaneados que no
// tenían que volver en esta rendición, o que no existen (A2).
internal sealed record ComparacionRendicion(RendicionPendiente Rendicion, IReadOnlyList<LineaComparada> Lineas,
    IReadOnlyList<string> Sobrantes)
{
    public int Faltantes => Lineas.Count(l => l.Faltante);
}

internal sealed record RendicionConfirmada(int Devueltos, int Reintegrados, int Faltantes);
internal sealed record RendicionEnListado(RendicionPendiente Rendicion, string Chofer, string Vehiculo);

// CU-55, nivel mínimo: el operario compara lo que volvió con lo declarado y confirma. Depósito coordina; cada módulo
// cambia lo suyo por su contrato (ADR-0001, addendum 3), todo en una sola transacción: Ejecución cierra la rendición,
// Envíos reintegra los no intentados (T8) y registra los faltantes, y Planificación finaliza la ruta.
internal sealed class ConfirmarRendicionHandler(
    IEjecucionModuleApi ejecucion,
    IEnviosModuleApi envios,
    IPlanificacionModuleApi planificacion,
    IAdministracionModuleApi administracion,
    IUnidadDeTrabajo unidad)
{
    public async Task<IReadOnlyList<RendicionEnListado>> PendientesAsync(int pagina, CancellationToken ct)
    {
        var pendientes = await ejecucion.ListarRendicionesPendientesAsync(pagina, ct);
        if (pendientes.Count == 0) return [];
        var vehiculosPorRuta = new Dictionary<Guid, Guid>();
        foreach (var rendicion in pendientes)
        {
            if (await planificacion.ConsultarHojaAsync(rendicion.RutaId, rendicion.RepartidorId, ct) is { } ruta)
                vehiculosPorRuta[ruta.Id] = ruta.VehiculoId;
        }
        var nombres = await administracion.ConsultarNombresRecursosAsync(
            pendientes.Select(r => r.RepartidorId).Distinct().ToArray(), vehiculosPorRuta.Values.Distinct().ToArray(), ct);
        return pendientes.Select(r => new RendicionEnListado(r,
            nombres.Repartidores.GetValueOrDefault(r.RepartidorId, "No asignado"),
            nombres.Vehiculos.GetValueOrDefault(vehiculosPorRuta.GetValueOrDefault(r.RutaId), "No asignado"))).ToArray();
    }

    // null si la ruta no tiene rendición o es de otro operador: la página responde 404.
    public async Task<ComparacionRendicion?> CompararAsync(Guid rutaId, IReadOnlyCollection<string> codigos,
        CancellationToken ct)
    {
        return await ejecucion.ConsultarRendicionAsync(rutaId, ct) is { } rendicion
            ? await CompararAsync(rendicion, codigos, ct)
            : null;
    }

    public async Task<RendicionConfirmada?> ConfirmarAsync(Guid rutaId, IReadOnlyCollection<string> codigos,
        CancellationToken ct)
    {
        RendicionConfirmada? resultado = null;

        await unidad.EjecutarAsync(async () =>
        {
            // El mismo bloqueo por operador que toman la planificación y las operaciones del repartidor: nadie cambia
            // la ruta ni sus envíos mientras se cierra.
            await administracion.BloquearPlanificacionAsync(ct);

            if (await ejecucion.ConsultarRendicionAsync(rutaId, ct) is not { } rendicion)
            {
                return;
            }

            if (rendicion.FinalizadaEn is not null)
            {
                throw new DomainException("La rendición ya se confirmó.");
            }

            // A2: un sobrante se revisa antes de cerrar.
            var comparacion = await CompararAsync(rendicion, codigos, ct);
            if (comparacion.Sobrantes.Count > 0)
            {
                throw new DomainException(
                    $"Hay bultos que no están en esta rendición: {string.Join(", ", comparacion.Sobrantes)}. Revisalos y sacalos de la lista antes de confirmar.");
            }

            var recibidos = comparacion.Lineas.Where(l => l.Escaneado).Select(l => l.Linea.BultoId).ToArray();
            var cerrada = await ejecucion.ConfirmarRendicionAsync(rutaId, recibidos, ct)
                ?? throw new InvalidOperationException("La rendición dejó de existir mientras se confirmaba.");

            // T8: un envío que no llegó a intentarse vuelve al depósito si volvieron todos sus bultos. Los de un intento
            // fallido ya se resolvieron en CU-18: acá sólo se registra que volvieron (requerimiento especial 1).
            var reintegrados = cerrada.Lineas.GroupBy(l => l.EnvioId)
                .Where(g => g.All(l => l.Situacion == "NoIntentado" && l.Recibida == true))
                .Select(g => g.Key).ToArray();
            await envios.ReintegrarADepositoAsync(reintegrados, responsableId: null, ct);

            // A1: los faltantes quedan como incidencia del envío; el extravío lo decide el despachador (CU-21).
            var faltantes = cerrada.Lineas.Where(l => l.Recibida == false).Select(l => l.BultoId).ToArray();
            await envios.RegistrarFaltantesAsync(faltantes, ct);

            // CU-31: un envío en devolución cuyos bultos volvieron ya está en el depósito, listo para entregar al
            // comercio. Envíos ignora los que no tienen devolución.
            var conBultosDevueltos = cerrada.Lineas.Where(l => l.Recibida == true).Select(l => l.EnvioId).Distinct().ToArray();
            await envios.RegistrarDevolucionesRecibidasAsync(conBultosDevueltos, ct);

            var entregados = cerrada.Lineas.GroupBy(l => l.EnvioId)
                .Where(g => g.All(l => l.EstadoFinal == "EntregadoEnCalle"))
                .Select(g => g.Key).ToArray();
            await planificacion.FinalizarRutaAsync(rutaId, entregados, ct);

            resultado = new RendicionConfirmada(recibidos.Length, reintegrados.Length, faltantes.Length);
        }, ct);

        return resultado;
    }

    // El lector escribe el código como un teclado: se compara sin espacios y sin distinguir mayúsculas.
    private async Task<ComparacionRendicion> CompararAsync(RendicionPendiente rendicion,
        IReadOnlyCollection<string> codigos, CancellationToken ct)
    {
        var envioIds = rendicion.Lineas.Select(l => l.EnvioId).Distinct().ToArray();
        var codigoDe = (await envios.ConsultarCargaAsync(envioIds, ct))
            .SelectMany(e => e.Bultos).ToDictionary(b => b.Id, b => b.Codigo);
        var escaneados = codigos.Select(c => c.Trim()).Where(c => c.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var lineas = rendicion.Lineas.Select(l =>
        {
            var codigo = codigoDe.GetValueOrDefault(l.BultoId, "?");
            return new LineaComparada(l, codigo, l.DebeVolver && escaneados.Contains(codigo));
        }).OrderBy(l => l.Codigo).ToList();

        var esperados = lineas.Where(l => l.Linea.DebeVolver).Select(l => l.Codigo)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sobrantes = escaneados.Where(c => !esperados.Contains(c)).Order(StringComparer.OrdinalIgnoreCase).ToList();

        return new ComparacionRendicion(rendicion, lineas, sobrantes);
    }
}

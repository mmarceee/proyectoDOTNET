using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Ejecucion.Contracts;
using Logistica.Modules.Ejecucion.Contracts.Results;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Planificacion.Contracts;
using Logistica.SharedKernel;
using System.Text.Json;

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
    IUnidadDeTrabajo unidad, IResponsableDeposito responsable, IOperacionesDeposito operaciones)
{
    public async Task<IReadOnlyList<RendicionEnListado>> ListarAsync(int pagina, bool confirmadas, CancellationToken ct)
    {
        var pendientes = confirmadas
            ? await ejecucion.ListarRendicionesConfirmadasAsync(pagina, ct)
            : await ejecucion.ListarRendicionesPendientesAsync(pagina, ct);
        if (pendientes.Count == 0) return [];
        var vehiculosPorRuta = new Dictionary<Guid, Guid>();
        foreach (var rendicion in pendientes)
        {
            // Esta consulta admite rutas finalizadas; no exige permisos de pantalla de Despacho.
            if (await planificacion.ConsultarParaPosicionAsync(rendicion.RutaId, rendicion.RepartidorId, ct) is { } ruta)
                vehiculosPorRuta[rendicion.RutaId] = ruta.VehiculoId;
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
        CancellationToken ct, bool confirmarNoCargadosEnDeposito = false, long? revisionEsperada = null, Guid? operacionId = null)
    {
        RendicionConfirmada? resultado = null;
        var operacion = operacionId ?? Guid.NewGuid();
        var huella = JsonSerializer.Serialize(new { Tipo = "ConfirmarRendicion", RutaId = rutaId, Revision = revisionEsperada,
            Codigos = codigos.Select(c => c.Trim().ToUpperInvariant()).Distinct().Order().ToArray(),
            ConfirmarNoCargados = confirmarNoCargadosEnDeposito, Actor = responsable.Id });

        await unidad.EjecutarAsync(async () =>
        {
            // El mismo bloqueo por operador que toman la planificación y las operaciones del repartidor: nadie cambia
            // la ruta ni sus envíos mientras se cierra.
            await administracion.BloquearPlanificacionAsync(ct);
            if (await operaciones.ResultadoAsync(operacion, huella, ct) is { } anterior)
            { resultado = JsonSerializer.Deserialize<RendicionConfirmada>(anterior)!; return; }
            if (await ejecucion.ConsultarRendicionAsync(rutaId, ct) is not { } rendicion)
            {
                return;
            }
            if (revisionEsperada is not long esperada || await planificacion.RevisionAsync(rutaId, ct) != esperada)
                throw new DomainException("La ruta cambió. Recargá antes de confirmar la rendición.");

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

            // T8 por bulto sin intento. Un retorno tras intento sólo cambia custodia y cierra su reserva.
            var recibidosFisicos = cerrada.Lineas.Where(l => l.Recibida == true && l.DebeVolver).Select(l => l.BultoId).ToArray();
            await ConfirmarRetornoAsync(cerrada, recibidosFisicos, "Recepción física en rendición", ct, operacion);
            var reintegrados = cerrada.Lineas.Where(l => recibidosFisicos.Contains(l.BultoId) && l.Situacion == "NoIntentado")
                .Select(l => l.EnvioId).Distinct().ToArray();

            // A1: los faltantes quedan como incidencia del envío; el extravío lo decide el despachador (CU-21).
            var faltantes = cerrada.Lineas.Where(l => l.Recibida == false).Select(l => l.BultoId).ToArray();
            await envios.RegistrarFaltantesAsync(faltantes, ct);
            if (faltantes.Length > 0)
            {
                var custodias = (await envios.ConsultarCustodiaAsync(cerrada.Lineas.Select(l => l.EnvioId).Distinct().ToArray(), ct))
                    .Where(b => faltantes.Contains(b.BultoId)
                        && b.Estado is "AsignadoARuta" or "EnTransito" or "Reprogramado" or "EnDevolucion").ToArray();
                if (custodias.Length > 0)
                    await envios.ConfirmarCustodiaAsync(new ConciliacionBultos(operacion, rutaId, null,
                        custodias.Select(b => new ConfirmacionCustodiaBulto(b.BultoId, b.Revision)).ToArray(), false,
                        "Bulto no localizado al confirmar rendición", responsable.Id), ct);
            }

            // CU-31: un envío en devolución cuyos bultos volvieron ya está en el depósito, listo para entregar al
            // comercio. Envíos ignora los que no tienen devolución.
            var conBultosDevueltos = cerrada.Lineas.Where(l => l.Recibida == true).Select(l => l.EnvioId).Distinct().ToArray();
            await envios.RegistrarDevolucionesRecibidasAsync(conBultosDevueltos, ct);

            var entregados = cerrada.Lineas.GroupBy(l => l.EnvioId)
                .Where(g => g.All(l => l.EstadoFinal == "EntregadoEnCalle"))
                .Select(g => g.Key).ToArray();
            await planificacion.FinalizarRutaAsync(rutaId, entregados, ct);

            // T4 sólo con confirmación física explícita; el cierre de ruta por sí mismo conserva las reservas.
            if (confirmarNoCargadosEnDeposito) await LiberarNoCargadosAsync(cerrada, ct, operacion);

            resultado = new RendicionConfirmada(recibidos.Length, reintegrados.Length, faltantes.Length);
            await operaciones.RegistrarAsync(operacion, huella, JsonSerializer.Serialize(resultado), ct);
        }, ct);

        return resultado;
    }

    // Recuperación explícita para cierres anteriores al arreglo. Nunca se escribe al consultar una página.
    public async Task<int?> RecuperarNoCargadosAsync(Guid rutaId, CancellationToken ct, long? revisionEsperada = null, Guid? operacionId = null)
    {
        int? cantidad = null;
        var operacion = operacionId ?? Guid.NewGuid();
        var huella = JsonSerializer.Serialize(new { Tipo = "RecuperarNoCargados", RutaId = rutaId, Revision = revisionEsperada, Actor = responsable.Id });
        await unidad.EjecutarAsync(async () =>
        {
            await administracion.BloquearPlanificacionAsync(ct);
            if (await operaciones.ResultadoAsync(operacion, huella, ct) is { } anterior)
            { cantidad = JsonSerializer.Deserialize<int>(anterior); return; }
            if (await ejecucion.ConsultarRendicionAsync(rutaId, ct) is not { } rendicion) return;
            var revision = await planificacion.RevisionAsync(rutaId, ct);
            if (revisionEsperada is not long esperada || revision != esperada)
                throw new DomainException("La ruta cambió. Recargá antes de recuperar los no cargados.");
            if (rendicion.FinalizadaEn is null)
                throw new DomainException("Primero confirmá la rendición de la ruta.");
            cantidad = await LiberarNoCargadosAsync(rendicion, ct, operacion);
            if (cantidad > 0 && revision is long actual) await planificacion.RegistrarConciliacionAsync(rutaId, actual, ct);
            await operaciones.RegistrarAsync(operacion, huella, JsonSerializer.Serialize(cantidad), ct);
        }, ct);
        return cantidad;
    }

    public async Task<IReadOnlyList<Guid>> NoCargadosRecuperablesAsync(RendicionPendiente rendicion, CancellationToken ct)
    {
        if (rendicion.FinalizadaEn is null) return [];
        var manifiesto = (await planificacion.ConsultarReservasRutaAsync(rendicion.RutaId, ct)).Where(r => r.CerradaEn == null).ToArray();
        var candidatos = rendicion.Lineas.Where(l => manifiesto.Any(r => r.BultoId == l.BultoId)
            && (l.Situacion == "NoCargado" || l.Situacion == "CargaParcial" && l.Recibida == true)).ToArray();
        if (candidatos.Length == 0) return [];
        var custodias = await envios.ConsultarCustodiaAsync(candidatos.Select(l => l.EnvioId).Distinct().ToArray(), ct);
        return candidatos.Where(l => custodias.Any(b => b.BultoId == l.BultoId && b.Estado == "AsignadoARuta"))
            .Select(l => l.EnvioId).Distinct().ToArray();
    }

    private async Task<int> LiberarNoCargadosAsync(RendicionPendiente rendicion, CancellationToken ct, Guid operacion)
    {
        var manifiesto = (await planificacion.ConsultarReservasRutaAsync(rendicion.RutaId, ct)).Where(r => r.CerradaEn == null).ToArray();
        var ids = rendicion.Lineas.Where(l => manifiesto.Any(r => r.BultoId == l.BultoId)
            && (l.Situacion == "NoCargado" || l.Situacion == "CargaParcial" && l.Recibida == true))
            .Select(l => l.BultoId).ToArray();
        await ConfirmarRetornoAsync(rendicion, ids, "Presencia física de no cargados confirmada por depósito", ct, operacion);
        return rendicion.Lineas.Where(l => ids.Contains(l.BultoId)).Select(l => l.EnvioId).Distinct().Count();
    }

    private async Task ConfirmarRetornoAsync(RendicionPendiente rendicion, IReadOnlyCollection<Guid> ids, string motivo, CancellationToken ct, Guid operacion)
    {
        if (ids.Count == 0) return;
        var custodias = (await envios.ConsultarCustodiaAsync(rendicion.Lineas.Select(l => l.EnvioId).Distinct().ToArray(), ct))
            .Where(b => ids.Contains(b.BultoId)).ToArray();
        await envios.ConfirmarCustodiaAsync(new ConciliacionBultos(operacion, rendicion.RutaId, null,
            custodias.Select(b => new ConfirmacionCustodiaBulto(b.BultoId, b.Revision)).ToArray(), true, motivo, responsable.Id), ct);
        await envios.ReintegrarBultosAsync(rendicion.RutaId, ids, responsable.Id, ct);
    }

    // El lector escribe el código como un teclado: se compara sin espacios y sin distinguir mayúsculas.
    private async Task<ComparacionRendicion> CompararAsync(RendicionPendiente rendicion,
        IReadOnlyCollection<string> codigos, CancellationToken ct)
    {
        var envioIds = rendicion.Lineas.Select(l => l.EnvioId).Distinct().ToArray();
        // Todos los bultos, no sólo los que acompañan al envío (RF 31): un bulto que quedó en devolución mientras el envío
        // sigue Reprogramado también vuelve en la rendición.
        var codigoDe = (await envios.ConsultarParaRendicionAsync(envioIds, ct))
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

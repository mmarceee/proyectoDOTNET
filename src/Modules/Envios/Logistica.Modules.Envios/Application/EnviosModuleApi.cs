using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Envios.Application.Features.ConsultarEnvios;
using Logistica.Modules.Envios.Domain.Devoluciones;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Domain.Incidencias;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Application;

// Implementación del contrato de Envíos. Es internal: los otros módulos sólo ven la interfaz.
internal sealed class EnviosModuleApi(IEnvioRepository envios, TimeProvider reloj, IReglasDeEntrega reglasDeEntrega,
    IIntegrationEventPublisher eventos, ConsultarEnviosHandler listado,
    Features.ReprogramarPorDestinatario.ReprogramarPorDestinatarioHandler reprogramar,
    Features.IniciarDevolucion.DevolverReprogramadosVencidosHandler vencidos) : IEnviosModuleApi
{
    public Task<int> DevolverReprogramadosVencidosAsync(DateOnly hoy, CancellationToken ct) => vencidos.HandleAsync(hoy, ct);

    public async Task<IReadOnlyList<EnvioLiquidable>> ListarLiquidablesAsync(Guid comercioId, DateTimeOffset desde,
        DateTimeOffset hasta, CancellationToken ct)
    {
        var cerrados = await envios.ListarCerradosAsync(comercioId, desde, hasta, ct);
        return cerrados.Select(c => new EnvioLiquidable(c.EnvioId, c.Numero, c.Estado.ToString(), c.LlegoEn, c.MontoTarifa))
            .ToArray();
    }

    // CU-61: el seguimiento público pide las opciones y la reprogramación por este contrato.
    public Task<IReadOnlyList<OpcionReprogramacion>?> OpcionesReprogramacionAsync(Guid envioId, CancellationToken ct)
        => reprogramar.OpcionesAsync(envioId, ct);

    public Task<bool> ReprogramarPorDestinatarioAsync(Guid envioId, DateOnly fecha, Guid franjaId, CancellationToken ct)
        => reprogramar.ReprogramarAsync(envioId, fecha, franjaId, ct);

    public async Task<IReadOnlyList<EnvioParaCarga>> ConsultarCargaAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var lista = await envios.ObtenerParaPlanificacionAsync(ids, ct);
        return lista.Select(e => new EnvioParaCarga(e.Id, e.Estado.ToString(),
            e.Bultos.Select(b => new BultoParaCarga(b.Id, b.Codigo)).ToArray()) { Numero = e.Numero }).ToArray();
    }

    public async Task<IReadOnlyList<BultoParaCarga>> ReconocerBultosAsync(IReadOnlyCollection<string> codigos, CancellationToken ct)
    {
        var bultos = await envios.ObtenerBultosPorCodigosAsync(codigos, ct);
        return bultos.Select(b => new BultoParaCarga(b.Id, b.Codigo)).ToArray();
    }

    public async Task<bool> RegistrarCargaAsync(IReadOnlyCollection<Guid> ids, Guid? responsable, DateTimeOffset ocurridoEn, CancellationToken ct)
    {
        var lista = await envios.ObtenerParaPlanificacionAsync(ids, ct);
        // Validar todos antes de modificar alguno: una carga rechazada no deja transiciones parciales.
        if (lista.Count != ids.Count || lista.Any(e => e.Estado != EstadoEnvio.AsignadoARuta)) return false;
        foreach (var envio in lista)
        {
            envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, responsable, ocurridoEn);
            eventos.Registrar(EnvioEnTransito.Tipo,
                new EnvioEnTransito(envio.Id, envio.Numero, ocurridoEn, responsable), ocurridoEn);
        }
        await envios.GuardarCambiosAsync(ct);
        return true;
    }

    // ADR-0004, sección 2.11: Ejecución clasifica cada bulto de la visita con su estado actual antes de registrarla.
    public async Task<EnvioParaVisita?> ConsultarParaVisitaAsync(Guid envioId, CancellationToken ct)
    {
        if (await envios.ObtenerConBultosAsync(envioId, ct) is not { } envio)
        {
            return null;
        }

        var reglas = await reglasDeEntrega.DelEnvioAsync(envio, ct);
        return new EnvioParaVisita(envio.Id,
            envio.Bultos.Select(b => new BultoParaVisita(b.Id, b.Codigo, b.Estado.ToString())).ToList(),
            reglas.AdmiteEntregaParcial);
    }

    // CU-17 y CU-18 por bulto (RF 31 y 32): la visita de un envío se aplica entera o no se aplica (Envio.RegistrarVisita).
    // Las reglas de negocio no lanzan hacia afuera: vuelven como resultado, para que Ejecución aplique la política de
    // conflictos (ADR-0004, sección 2.4). Los archivos se agregan recién cuando el dominio aceptó la visita.
    public async Task<RegistroEnCalle> RegistrarVisitaAsync(VisitaARegistrar visita, CancellationToken ct)
    {
        var envio = await envios.ObtenerConIntentosAsync(visita.EnvioId, ct);
        if (envio is null)
        {
            return new(ResultadoRegistro.EnvioNoEncontrado);
        }

        try
        {
            var reglas = await reglasDeEntrega.DelEnvioAsync(envio, ct);
            var motivos = new Dictionary<Guid, MotivoNoEntrega>();
            foreach (var motivoId in visita.Resultados.Select(r => r.MotivoId).OfType<Guid>().Distinct())
            {
                motivos[motivoId] = await reglasDeEntrega.MotivoAsync(envio, motivoId, ct)
                    ?? throw new DomainException("El motivo no está en el catálogo de las reglas del envío.");
            }

            var ubicacion = new Ubicacion(visita.Latitud, visita.Longitud);
            var firma = visita.Firma is null ? null
                : ArchivoEvidencia.Crear(envio, visita.Firma.TipoContenido, visita.Firma.Contenido);
            var foto = visita.Foto is null ? null
                : ArchivoEvidencia.Crear(envio, visita.Foto.TipoContenido, visita.Foto.Contenido);
            // La prueba de entrega lleva la firma; la evidencia del intento fallido, sólo la foto (como en CU-17 y CU-18).
            var prueba = firma is null ? null : new PruebaEntrega(firma.Id, foto?.Id, visita.NombreReceptor,
                visita.DocumentoReceptor, ubicacion, visita.OcurridoEn);
            var evidencia = foto is null ? null : new PruebaEntrega(null, foto.Id, null, null, ubicacion, visita.OcurridoEn);

            var intentos = envio.RegistrarVisita(
                visita.Resultados.Select(r => new ResultadoBulto(r.BultoId, r.MotivoId is Guid id ? motivos[id] : null)).ToList(),
                prueba, evidencia, visita.Observaciones, ubicacion, reglas, visita.ResponsableId, visita.OcurridoEn);

            if (firma is not null)
            {
                envios.Agregar(firma);
            }

            if (foto is not null)
            {
                envios.Agregar(foto);
            }

            // Con todos sus pendientes devueltos, el envío queda EnDevolucion e inicia la Devolucion (CU-20), como antes.
            // Un bulto devuelto mientras otros se reprograman espera la devolución por bulto (pendiente 2 de la decisión).
            if (envio.Estado == EstadoEnvio.EnDevolucion)
            {
                var motivo = motivos.Values.First();
                envios.Agregar(Devolucion.Crear(envio, $"Intento fallido: {motivo.Codigo}.", visita.OcurridoEn));
            }

            // Avisos v1 (pendiente 6): EnvioEntregado sólo si se entregaron todos los bultos; si quedó alguno, el intento
            // fallido. Un comercio v1 nunca recibe una entrega completa que no ocurrió.
            if (intentos.All(i => i.Resultado == ResultadoIntento.Exitoso))
            {
                var entregadoEn = envio.Eventos[^1].OcurridoEn;
                eventos.Registrar(EnvioEntregado.Tipo,
                    new EnvioEntregado(envio.Id, envio.Numero, entregadoEn, visita.ResponsableId, visita.RepartidorId), entregadoEn);
            }
            else
            {
                foreach (var fallido in intentos.Where(i => i.Resultado == ResultadoIntento.Fallido))
                {
                    eventos.Registrar(IntentoFallidoRegistrado.Tipo,
                        new IntentoFallidoRegistrado(envio.Id, envio.Numero, fallido.NumeroIntento,
                            motivos[fallido.MotivoNoEntregaId!.Value].Codigo, envio.Estado.ToString(), fallido.FechaHora,
                            envio.ReintentoDesde, visita.RepartidorId),
                        fallido.FechaHora);
                }
            }

            await envios.GuardarCambiosAsync(ct);
            return new(ResultadoRegistro.Aplicado, envio.Estado.ToString());
        }
        catch (TransicionInvalidaException)
        {
            return new(ResultadoRegistro.Conflicto, envio.Estado.ToString());
        }
        catch (DomainException ex)
        {
            return new(ResultadoRegistro.Invalido, envio.Estado.ToString(), ex.Message);
        }
    }

    public Task<bool> HayEnviosComprometidosAsync(IReadOnlyCollection<Guid> franjaIds, DateOnly desde, CancellationToken ct)
    {
        return envios.HayComprometidosAsync(franjaIds, desde, ct);
    }

    // CU-60: sólo lo que el destinatario puede ver (requerimientos 1 y 3 de la ficha).
    public async Task<SeguimientoEnvio?> SeguimientoDeAsync(Guid envioId, CancellationToken ct)
    {
        if (await envios.ObtenerConEventosAsync(envioId, ct) is not Envio envio)
        {
            return null;
        }

        var nombreDePila = envio.Destinatario.Nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return new SeguimientoEnvio(envio.Id, envio.Numero, envio.Estado.ToString(), nombreDePila,
            envio.Direccion.Localidad, envio.CreadoEn, envio.FechaEntregaProgramada, envio.FranjaHorariaId,
            envio.VersionReglasId,
            envio.Eventos.OrderBy(e => e.OcurridoEn)
                .Select(e => new EventoSeguimiento(e.EstadoNuevo.ToString(), e.OcurridoEn)).ToList());
    }

    public Task<Guid?> IdPorNumeroAsync(string numero, CancellationToken ct)
    {
        return envios.IdPorNumeroAsync(numero.Trim().ToUpperInvariant(), ct);
    }

    // CU-60: reusa el listado del Backoffice (CU-13), con sus validaciones y el filtro "Tenant", y devuelve sólo lo que
    // la página de seguimiento muestra.
    public async Task<PaginaEnviosParaSeguimiento> ListarParaSeguimientoAsync(string? texto, int pagina, CancellationToken ct)
    {
        var resultado = await listado.HandleAsync(new ConsultarEnviosQuery(Pagina: pagina, Texto: texto), ct);
        var filas = resultado.Envios
            .Select(e => new EnvioParaSeguimiento(e.Numero, e.Estado, e.DestinatarioNombre, e.Localidad, e.CreadoEn))
            .ToArray();
        return new PaginaEnviosParaSeguimiento(filas, resultado.Pagina, resultado.TotalPaginas);
    }

    // CU-66: traduce los envíos a lo que ve el tablero. "EntregaPendiente" usa la misma lista que CU-04.
    public async Task<IReadOnlyList<EnvioParaTablero>> ListarParaTableroAsync(IReadOnlyCollection<Guid> deRutas,
        DateOnly hasta, CancellationToken ct)
    {
        return (await envios.ListarParaTableroAsync(deRutas, hasta, ct))
            .Select(e => new EnvioParaTablero(e.Id, e.Numero, e.Estado.ToString(), e.FechaEntregaProgramada,
                e.FranjaHorariaId, Envio.EstadosConEntregaPendiente.Contains(e.Estado)))
            .ToArray();
    }

    public Task<IReadOnlyList<Guid>> EnviosSinFechaConFranjasAsync(IReadOnlyCollection<Guid> franjaIds, CancellationToken ct)
    {
        return envios.SinFechaConFranjasAsync(franjaIds, ct);
    }

    public async Task<IReadOnlyList<EnvioPlanificable>> ObtenerParaPlanificacionAsync(IReadOnlyCollection<Guid>? envioIds, CancellationToken ct)
    {
        var lista = await envios.ObtenerParaPlanificacionAsync(envioIds, ct);
        return lista.Select(e => new EnvioPlanificable(e.Id, e.OperadorId, e.Numero, e.Estado.ToString(), e.ZonaId,
            e.FranjaHorariaId, e.FechaEntregaProgramada, $"{e.Direccion.Calle} {e.Direccion.Numero}, {e.Direccion.Localidad}",
            e.Direccion.CodigoPostal, e.Bultos.Select(b => new BultoPlanificable(b.Id, b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm)).ToList(),
            new PuntoEntregaPlanificable(e.Direccion.Calle, e.Direccion.Numero, e.Direccion.Localidad, e.Direccion.Departamento, e.Direccion.CodigoPostal),
            e.ReintentoDesde, e.Modalidad.ToString())).ToList();
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
            envios.RegistrarAsignacion(envio.Id, rutaId, responsableId, ahora);
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

    public async Task DesasignarDeRutaAsync(Guid rutaId, Guid envioId, Guid? responsableId, CancellationToken ct)
    {
        var envio = await envios.ObtenerAsync(envioId, ct) ?? throw new DomainException("El envío no está disponible.");
        if (envio.Estado != EstadoEnvio.AsignadoARuta)
            throw new DomainException("Sólo se puede desasignar un envío que esté AsignadoARuta.");
        var ahora = reloj.GetUtcNow();
        // T4 conserva zona, franja y fecha programada; sólo cambia el estado y registra el hecho.
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, responsableId, ahora,
            detalle: $"Quitado de la ruta {rutaId}.");
        envios.RegistrarDesasignacion(envio.Id, rutaId, responsableId, ahora);
        await envios.GuardarCambiosAsync(ct);
    }

    public async Task<bool> RecibirBultoAsync(Guid envioId, Guid bultoId, Guid? responsableId, CancellationToken ct)
    {
        var envio = await envios.ObtenerAsync(envioId, ct)
            ?? throw new DomainException("El envío no existe.");

        // T2 por bulto. Si el envío ya no está Admitido (por ejemplo, lo cancelaron), el dominio lo rechaza.
        var ahora = reloj.GetUtcNow();
        var completo = envio.RecibirBulto(bultoId, responsableId, ahora);
        if (completo)
        {
            eventos.Registrar(EnvioRecibidoEnDeposito.Tipo,
                new EnvioRecibidoEnDeposito(envio.Id, envio.Numero, ahora, responsableId), ahora);
        }

        await envios.GuardarCambiosAsync(ct);
        return completo;
    }

    // CU-55 (T8). Si un envío ya no está EnTransito (lo entregaron o lo declararon extraviado mientras tanto),
    // Transicionar lo rechaza y no se reintegra ninguno: la rendición entera se confirma o no.
    public async Task ReintegrarADepositoAsync(IReadOnlyCollection<Guid> envioIds, Guid? responsableId, CancellationToken ct)
    {
        if (envioIds.Count == 0)
        {
            return;
        }

        var lista = await envios.ObtenerParaPlanificacionAsync(envioIds, ct);
        if (lista.Count != envioIds.Distinct().Count())
        {
            throw new DomainException("Un envío de la rendición ya no existe.");
        }

        var ahora = reloj.GetUtcNow();
        foreach (var envio in lista)
        {
            envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, responsableId, ahora,
                detalle: "Volvió en la rendición sin haberse intentado.");
            eventos.Registrar(EnvioReintegradoADeposito.Tipo,
                new EnvioReintegradoADeposito(envio.Id, envio.Numero, ahora, responsableId), ahora);
        }

        await envios.GuardarCambiosAsync(ct);
    }

    // CU-55, A1: una incidencia por envío, con los códigos de los bultos que no volvieron. El envío no cambia de estado:
    // el extravío lo declara el despachador (CU-21).
    public async Task RegistrarFaltantesAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct)
    {
        if (bultoIds.Count == 0)
        {
            return;
        }

        var ahora = reloj.GetUtcNow();
        foreach (var envio in await envios.ObtenerPorBultosAsync(bultoIds, ct))
        {
            var codigos = envio.Bultos.Where(b => bultoIds.Contains(b.Id)).Select(b => b.Codigo).Order();
            envios.Agregar(Incidencia.Crear(envio, TipoIncidencia.Extravio,
                $"Faltante en la rendición. Bultos que no volvieron: {string.Join(", ", codigos)}. Revisar si se declara el extravío (CU-21).",
                ahora));
        }

        await envios.GuardarCambiosAsync(ct);
    }

    public async Task RegistrarDevolucionesRecibidasAsync(IReadOnlyCollection<Guid> envioIds, CancellationToken ct)
    {
        if (envioIds.Count == 0)
        {
            return;
        }

        var ahora = reloj.GetUtcNow();
        foreach (var devolucion in await envios.ObtenerDevolucionesAsync(envioIds, ct))
        {
            if (devolucion.Estado != EstadoDevolucion.Cerrada)
            {
                devolucion.MarcarRecibida(ahora);
            }
        }

        await envios.GuardarCambiosAsync(ct);
    }

    public async Task<DevolucionParaEntrega?> ConsultarDevolucionAsync(string numeroOCodigo, CancellationToken ct)
    {
        // El lector escribe el código como un teclado: se busca sin espacios y en mayúsculas.
        var texto = numeroOCodigo.Trim().ToUpperInvariant();
        if (texto.Length == 0)
        {
            return null;
        }

        var envio = await envios.IdPorNumeroAsync(texto, ct) is Guid id
            ? await envios.ObtenerConBultosAsync(id, ct)
            : await envios.ObtenerPorCodigoBultoAsync(texto, ct);
        if (envio is null || await envios.ObtenerDevolucionAsync(envio.Id, ct) is not { } devolucion)
        {
            return null;
        }

        return new DevolucionParaEntrega(envio.Id, envio.Numero, envio.Estado.ToString(), devolucion.Motivo,
            devolucion.Estado == EstadoDevolucion.RecibidaEnDeposito, devolucion.Estado == EstadoDevolucion.Cerrada,
            envio.Bultos.OrderBy(b => b.Codigo).Select(b => new BultoDevuelto(b.Id, b.Codigo)).ToList());
    }

    public async Task ConfirmarDevolucionAsync(DevolucionAConfirmar confirmacion, CancellationToken ct)
    {
        var envio = await envios.ObtenerConBultosAsync(confirmacion.EnvioId, ct)
            ?? throw new DomainException("El envío no existe.");

        // A1: sólo un envío en devolución, con su devolución abierta.
        if (envio.Estado != EstadoEnvio.EnDevolucion || await envios.ObtenerDevolucionAsync(envio.Id, ct) is not { } devolucion)
        {
            throw new DomainException($"El envío {envio.Numero} no está en devolución.");
        }

        var entregados = confirmacion.BultosEntregados.ToHashSet();
        if (entregados.Count == 0)
        {
            throw new DomainException("Marcá los bultos que se entregan al comercio.");
        }

        if (!entregados.IsSubsetOf(envio.Bultos.Select(b => b.Id)))
        {
            throw new DomainException("Uno de los bultos marcados no es de este envío.");
        }

        var ahora = reloj.GetUtcNow();
        devolucion.Cerrar(confirmacion.NombreReceptor, confirmacion.DocumentoReceptor, ahora);
        envio.Transicionar(EstadoEnvio.Devuelto, OrigenEvento.Backoffice, confirmacion.ResponsableId, ahora,
            detalle: $"Lo recibió {devolucion.NombreReceptor} (documento {devolucion.DocumentoReceptor}) por el comercio.");

        // A2: la entrega parcial queda registrada como incidencia del envío, con los códigos que faltaron.
        var faltantes = envio.Bultos.Where(b => !entregados.Contains(b.Id)).Select(b => b.Codigo).Order().ToArray();
        if (faltantes.Length > 0)
        {
            envios.Agregar(Incidencia.Crear(envio, TipoIncidencia.Extravio,
                $"Devolución entregada al comercio sin los bultos: {string.Join(", ", faltantes)}.", ahora));
        }

        eventos.Registrar(EnvioDevuelto.Tipo, new EnvioDevuelto(envio.Id, envio.Numero, ahora, confirmacion.ResponsableId), ahora);
        await envios.GuardarCambiosAsync(ct);
    }
}

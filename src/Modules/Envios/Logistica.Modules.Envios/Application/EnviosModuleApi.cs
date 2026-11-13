using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Envios.Application.Features.ConsultarEnvios;
using Logistica.Modules.Envios.Domain.Devoluciones;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Domain.Incidencias;
using Logistica.SharedKernel;
using BultoDevuelto = Logistica.Modules.Envios.Contracts.Results.BultoDevuelto;
using EventoBultoDevuelto = Logistica.Modules.Envios.Contracts.Events.BultoDevuelto;

namespace Logistica.Modules.Envios.Application;

// Implementación del contrato de Envíos. Es internal: los otros módulos sólo ven la interfaz.
internal sealed partial class EnviosModuleApi(IEnvioRepository envios, TimeProvider reloj, IReglasDeEntrega reglasDeEntrega,
    IIntegrationEventPublisher eventos, ConsultarEnviosHandler listado,
    Features.ReprogramarPorDestinatario.ReprogramarPorDestinatarioHandler reprogramar,
    Features.IniciarDevolucion.DevolverReprogramadosVencidosHandler vencidos,
    Logistica.Modules.Planificacion.Contracts.IPlanificacionModuleApi reservas) : IEnviosModuleApi
{
    public Task<int> DevolverReprogramadosVencidosAsync(DateOnly hoy, CancellationToken ct) => vencidos.HandleAsync(hoy, ct);

    public async Task<IReadOnlyList<EnvioLiquidable>> ListarLiquidablesAsync(Guid comercioId, DateTimeOffset desde,
        DateTimeOffset hasta, CancellationToken ct)
    {
        var cerrados = await envios.ListarCerradosAsync(comercioId, desde, hasta, ct);
        return cerrados.Select(c => new EnvioLiquidable(c.EnvioId, c.Numero, c.Estado.ToString(), c.LlegoEn, c.MontoTarifa))
            .ToArray();
    }

    // RF 33: los recargos tal como se registraron; Envíos no los recalcula.
    public async Task<IReadOnlyList<RecargoLiquidable>> ListarRecargosAsync(Guid comercioId, DateTimeOffset desde,
        DateTimeOffset hasta, CancellationToken ct)
    {
        var recargos = await envios.ListarRecargosAsync(comercioId, desde, hasta, ct);
        return recargos.Select(r => new RecargoLiquidable(r.RecargoId, r.EnvioId, r.Numero, r.OcurridoEn, r.Monto)).ToArray();
    }

    // CU-61: el seguimiento público pide las opciones y la reprogramación por este contrato.
    public Task<IReadOnlyList<OpcionReprogramacion>?> OpcionesReprogramacionAsync(Guid envioId, CancellationToken ct)
        => reprogramar.OpcionesAsync(envioId, ct);

    public Task<bool> ReprogramarPorDestinatarioAsync(Guid envioId, DateOnly fecha, Guid franjaId, CancellationToken ct)
        => reprogramar.ReprogramarAsync(envioId, fecha, franjaId, ct);

    public async Task<IReadOnlyList<EnvioParaCarga>> ConsultarCargaAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        // La nueva visita excluye los ya entregados; una carga en preparación conserva los todavía asignados
        // aunque el primer lote cargado ya haya dejado el estado de compatibilidad EnTransito.
        var lista = await envios.ObtenerParaPlanificacionAsync(ids, ct);
        var resultado = new List<EnvioParaCarga>();
        foreach (var e in lista)
            resultado.Add(new(e.Id, e.Estado.ToString(), e.Bultos.Where(b => b.Estado is EstadoEnvio.AsignadoARuta or EstadoEnvio.EnTransito)
                .Select(b => new BultoParaCarga(b.Id, b.Codigo, b.Estado.ToString())).ToArray())
            { Numero = e.Numero, AdmiteEntregaParcial = (await reglasDeEntrega.ParaHojaAsync(e, ct)).AdmiteEntregaParcial });
        return resultado;
    }

    public async Task<bool> RegistrarCargaPorBultosAsync(IReadOnlyCollection<Guid> bultoIds, Guid? responsable, DateTimeOffset ocurridoEn, CancellationToken ct)
    {
        var lista = await envios.ObtenerPorBultosAsync(bultoIds, ct);
        if (bultoIds.Distinct().Count() != bultoIds.Count || lista.Sum(e => e.Bultos.Count(b => bultoIds.Contains(b.Id))) != bultoIds.Count) return false;
        foreach (var envio in lista)
        {
            var ids = envio.Bultos.Where(b => bultoIds.Contains(b.Id)).Select(b => b.Id).ToArray();
            var reglas = await reglasDeEntrega.ParaHojaAsync(envio, ct);
            if (!envio.PuedeCargarBultos(ids, reglas.AdmiteEntregaParcial)) return false;
        }
        foreach (var envio in lista)
        {
            envio.CargarBultos(envio.Bultos.Where(b => bultoIds.Contains(b.Id)).Select(b => b.Id).ToArray(), responsable, ocurridoEn);
            eventos.Registrar(EnvioEnTransito.Tipo, new EnvioEnTransito(envio.Id, envio.Numero, ocurridoEn, responsable), ocurridoEn);
        }
        await envios.GuardarCambiosAsync(ct);
        return true;
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
    public async Task<EnvioParaVisita?> ConsultarParaVisitaAsync(Guid envioId, CancellationToken ct, Guid? rutaId = null)
    {
        if (await envios.ObtenerConBultosAsync(envioId, ct) is not { } envio)
        {
            return null;
        }

        var reglas = await reglasDeEntrega.DelEnvioAsync(envio, ct);
        return new EnvioParaVisita(envio.Id,
            envio.Bultos.Select(b => new BultoParaVisita(b.Id, b.Codigo,
                rutaId is not null && b.Estado == EstadoEnvio.EnTransito && b.CustodiaRutaId != rutaId
                    ? "FueraDeRuta" : b.Estado.ToString())).ToList(),
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
                prueba, evidencia, visita.Observaciones, ubicacion, reglas, visita.ResponsableId, visita.OcurridoEn,
                visita.RutaId);

            if (firma is not null)
            {
                envios.Agregar(firma);
            }

            if (foto is not null)
            {
                envios.Agregar(foto);
            }

            // Un bulto que quedó en devolución inicia la Devolucion (CU-20), aunque otros del envío se reprogramen (RF 31,
            // pendiente 2 de la decisión): ese bulto vuelve al comercio solo (CU-31). Si el envío ya tiene una devolución
            // abierta, el bulto se suma a ella.
            var devuelto = visita.Resultados.FirstOrDefault(r => r.MotivoId is not null
                && envio.Bultos.Single(b => b.Id == r.BultoId).Estado == EstadoEnvio.EnDevolucion);
            if (devuelto is not null
                && await envios.ObtenerDevolucionAsync(envio.Id, ct) is not { Estado: not EstadoDevolucion.Cerrada })
            {
                var motivo = motivos[devuelto.MotivoId!.Value];
                envios.Agregar(Devolucion.Crear(envio, $"Intento fallido: {motivo.Codigo}.", visita.OcurridoEn));
            }

            // Avisos v1 (RF 36): EnvioEntregado sólo si el envío tiene todos sus bultos entregados, no sólo los de esta
            // visita (uno pudo quedar en devolución en una visita anterior). Un comercio v1 nunca recibe una entrega
            // completa que no ocurrió. Con pendientes, el intento fallido, como antes.
            if (envio.Bultos.All(b => b.Estado == EstadoEnvio.Entregado))
            {
                var entregadoEn = visita.OcurridoEn.ToUniversalTime();
                eventos.Registrar(EnvioEntregado.Tipo,
                    new EnvioEntregado(envio.Id, envio.Numero, entregadoEn, visita.ResponsableId, visita.RepartidorId), entregadoEn);
            }

            foreach (var fallido in intentos.Where(i => i.Resultado == ResultadoIntento.Fallido))
            {
                eventos.Registrar(IntentoFallidoRegistrado.Tipo,
                    new IntentoFallidoRegistrado(envio.Id, envio.Numero, fallido.NumeroIntento,
                        motivos[fallido.MotivoNoEntregaId!.Value].Codigo, envio.Estado.ToString(), fallido.FechaHora,
                        envio.ReintentoDesde, visita.RepartidorId),
                    fallido.FechaHora);
            }

            // Avisos v2 (RF 36): el resultado de cada bulto, además de los eventos del envío.
            var estadoEnvio = envio.Estado.ToString();
            foreach (var resultado in visita.Resultados)
            {
                var bulto = envio.Bultos.Single(b => b.Id == resultado.BultoId);
                if (resultado.MotivoId is Guid motivoId)
                {
                    eventos.Registrar(BultoNoEntregado.Tipo,
                        new BultoNoEntregado(envio.Id, envio.Numero, bulto.Codigo, motivos[motivoId].Codigo,
                            bulto.Estado.ToString(), estadoEnvio, visita.OcurridoEn, visita.RepartidorId),
                        visita.OcurridoEn);
                }
                else
                {
                    eventos.Registrar(BultoEntregado.Tipo,
                        new BultoEntregado(envio.Id, envio.Numero, bulto.Codigo, estadoEnvio, visita.OcurridoEn,
                            visita.RepartidorId),
                        visita.OcurridoEn);
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
                .Select(e => new EventoSeguimiento(e.EstadoNuevo.ToString(), e.OcurridoEn)).ToList(),
            envio.Resumen.ToString(), envio.Terminado, envio.BultosEntregados,
            // En el orden de la etiqueta (ENV-000001-2 antes que ENV-000001-10): la posición es el número del bulto.
            envio.Bultos.OrderBy(b => b.Codigo.Length).ThenBy(b => b.Codigo, StringComparer.Ordinal)
                .Select((b, i) => new BultoSeguimiento(i + 1, b.Estado.ToString())).ToList());
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

    // CU-66: traduce los envíos a lo que ve el tablero, decidiendo por bulto (D4): "EntregaPendiente" usa la misma
    // lista que CU-04 sobre cada bulto, y "Entregado" es el resumen, no el estado de compatibilidad.
    public async Task<IReadOnlyList<EnvioParaTablero>> ListarParaTableroAsync(IReadOnlyCollection<Guid> deRutas,
        DateOnly hasta, CancellationToken ct)
    {
        return (await envios.ListarParaTableroAsync(deRutas, hasta, ct))
            .Select(e => new EnvioParaTablero(e.Id, e.Numero, e.Estado.ToString(), e.FechaEntregaProgramada,
                e.FranjaHorariaId, e.TieneEntregaPendiente, e.Resumen == ResumenEnvio.Entregado,
                e.BultosEntregados, e.Bultos.Count))
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
            // Sólo los bultos que acompañan al envío: la capacidad del vehículo no cuenta los ya entregados (RF 34).
            e.Direccion.CodigoPostal, e.Bultos.Where(b => b.Estado is EstadoEnvio.EnDeposito or EstadoEnvio.Reprogramado or EstadoEnvio.AsignadoARuta)
                .Select(b => new BultoPlanificable(b.Id, b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm)
                { Codigo = b.Codigo, Estado = b.Estado.ToString(), Custodia = b.Custodia.ToString(), RevisionCustodia = b.RevisionCustodia,
                    ReintentoDesde = b.ReintentoDesde }).ToList(),
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
            var idsBultos = asignacion.BultoIds ?? envio.Bultos.Where(b => b.Estado is EstadoEnvio.EnDeposito or EstadoEnvio.Reprogramado)
                .Select(b => b.Id).ToArray();
            if (idsBultos.Count == 0 || idsBultos.Distinct().Count() != idsBultos.Count
                || idsBultos.Any(id => !envio.Bultos.Any(b => b.Id == id && b.Custodia == UbicacionCustodia.Deposito
                    && b.Estado is EstadoEnvio.EnDeposito or EstadoEnvio.Reprogramado)))
                throw new DomainException("Los bultos deben estar disponibles y confirmados físicamente en depósito.");
            envio.ResolverFranjaDeRuta(asignacion.FranjaId);
            envio.CambiarBultosDeRuta(idsBultos, EstadoEnvio.AsignadoARuta, responsableId, ahora);
            eventos.Registrar(PlanificacionBultosActualizada.Tipo,
                new PlanificacionBultosActualizada(envio.Id, rutaId, idsBultos.ToArray(), "Asignacion", ahora), ahora);
            if (envio.Bultos.Where(b => !Envio.EstadosTerminales.Contains(b.Estado)).All(b => b.Estado == EstadoEnvio.AsignadoARuta))
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
            PendienteDeRecepcion: envio.Bultos.Single(b => b.Codigo == codigoBulto).Estado == EstadoEnvio.Admitido,
            Cancelado: envio.Estado == EstadoEnvio.Cancelado,
            envio.Bultos
                .OrderBy(b => b.Codigo)
                .Select(b => new BultoDeclarado(b.Id, b.Codigo, b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm))
                .ToList());
    }

    public async Task DesasignarDeRutaAsync(Guid rutaId, Guid envioId, Guid? responsableId, CancellationToken ct)
    {
        var envio = await envios.ObtenerAsync(envioId, ct) ?? throw new DomainException("El envío no está disponible.");
        var ids = (await reservas.ConsultarReservasRutaAsync(rutaId, ct)).Where(r => r.EnvioId == envioId && r.CerradaEn == null)
            .Select(r => r.BultoId).ToArray();
        if (ids.Length == 0 || ids.Any(id => !envio.Bultos.Any(b => b.Id == id && b.Estado == EstadoEnvio.AsignadoARuta
            && b.Custodia == UbicacionCustodia.Deposito)))
            throw new DomainException("Sólo se pueden quitar bultos asignados a esta ruta y confirmados en depósito.");
        var ahora = reloj.GetUtcNow();
        // T4 conserva zona, franja y fecha programada; sólo cambia el estado y registra el hecho.
        envio.CambiarBultosDeRuta(ids, EstadoEnvio.EnDeposito, responsableId, ahora);
        eventos.Registrar(PlanificacionBultosActualizada.Tipo,
            new PlanificacionBultosActualizada(envio.Id, rutaId, ids, "RetiroDeRuta", ahora), ahora);
        if (envio.Bultos.Where(b => !Envio.EstadosTerminales.Contains(b.Estado)).All(b => b.Estado == EstadoEnvio.EnDeposito))
            envios.RegistrarDesasignacion(envio.Id, rutaId, responsableId, ahora);
        await envios.GuardarCambiosAsync(ct);
        await reservas.CerrarReservasBultosAsync(rutaId, ids, "QuitadoDeRutaPlanificada", ct);
    }

    public async Task<bool> RecibirBultoAsync(Guid envioId, Guid bultoId, Guid? responsableId, CancellationToken ct, Guid? operacionId = null)
    {
        var envio = await envios.ObtenerAsync(envioId, ct)
            ?? throw new DomainException("El envío no existe.");

        var ahora = reloj.GetUtcNow();
        var completo = envio.RecibirBulto(bultoId, responsableId, ahora);
        envio.Bultos.Single(b => b.Id == bultoId).ConfirmarCustodia(UbicacionCustodia.Deposito, null,
            responsableId, operacionId ?? Guid.NewGuid(), ahora, "Recepción física CU-30");
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

        var ids = lista.SelectMany(e => e.Bultos).Where(b => b.Estado == EstadoEnvio.EnTransito).Select(b => b.Id).ToArray();
        var activas = await reservas.ConsultarReservasActivasAsync(ids, ct);
        if (activas.Count != ids.Length) throw new DomainException("El reintegro requiere reservas inequívocas y confirmación física.");
        foreach (var grupo in activas.GroupBy(r => r.RutaId))
            await ReintegrarBultosAsync(grupo.Key, grupo.Select(r => r.BultoId).ToArray(), responsableId, ct);
    }

    // CU-55 (T4): lo que no salió vuelve a estar disponible para planificar. Los bultos ya entregados de una revisita no
    // se mueven, porque Transicionar sólo mueve los que están en el estado del envío.
    public async Task<int> ReintegrarNoCargadosAsync(Guid rutaId, IReadOnlyCollection<Guid> envioIds, CancellationToken ct)
    {
        if (envioIds.Count == 0)
        {
            return 0;
        }

        var propias = (await reservas.ConsultarReservasRutaAsync(rutaId, ct)).Where(r => r.CerradaEn == null && envioIds.Contains(r.EnvioId)).ToArray();
        var estados = await ConsultarCustodiaAsync(envioIds, ct);
        var ids = propias.Where(r => estados.Any(b => b.BultoId == r.BultoId && b.Estado == "AsignadoARuta"
            && b.Ubicacion == "Deposito")).Select(r => r.BultoId).ToArray();
        await ReintegrarBultosAsync(rutaId, ids, null, ct);
        return propias.Where(r => ids.Contains(r.BultoId)).Select(r => r.EnvioId).Distinct().Count();
    }

    public async Task<IReadOnlyList<EnvioParaRendicion>> ConsultarParaRendicionAsync(IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var lista = await envios.ObtenerParaPlanificacionAsync(ids, ct);
        return lista.Select(e => new EnvioParaRendicion(e.Id, e.Numero, e.Estado.ToString(),
            e.Bultos.Select(b => new BultoParaRendicion(b.Id, b.Codigo, b.Estado.ToString())).ToArray())).ToArray();
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
        var enviosDevueltos = await envios.ObtenerParaPlanificacionAsync(envioIds, ct);
        foreach (var devolucion in await envios.ObtenerDevolucionesAsync(envioIds, ct))
        {
            var pendientes = enviosDevueltos.Single(e => e.Id == devolucion.EnvioId).Bultos
                .Where(b => b.Estado == EstadoEnvio.EnDevolucion).ToArray();
            if (devolucion.Estado != EstadoDevolucion.Cerrada && pendientes.Length > 0
                && pendientes.All(b => b.Custodia == UbicacionCustodia.Deposito))
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

        // RF 31: sólo los bultos que vuelven al comercio. Los que ya recibió el destinatario no se entregan ni faltan.
        return new DevolucionParaEntrega(envio.Id, envio.Numero, envio.Estado.ToString(), devolucion.Motivo,
            devolucion.Estado == EstadoDevolucion.RecibidaEnDeposito, devolucion.Estado == EstadoDevolucion.Cerrada,
            BultosDeLaDevolucion(envio, devolucion).OrderBy(b => b.Codigo).Select(b => new BultoDevuelto(b.Id, b.Codigo)).ToList());
    }

    // Los bultos de la devolución: en devolución mientras está abierta, Devuelto una vez entregada al comercio. Con varias
    // devoluciones (RF 31), los Devuelto de una anterior no son de la abierta.
    private static IEnumerable<Bulto> BultosDeLaDevolucion(Envio envio, Devolucion devolucion) =>
        envio.Bultos.Where(b => b.Estado == (devolucion.Estado == EstadoDevolucion.Cerrada ? EstadoEnvio.Devuelto : EstadoEnvio.EnDevolucion));

    public async Task ConfirmarDevolucionAsync(DevolucionAConfirmar confirmacion, CancellationToken ct)
    {
        var envio = await envios.ObtenerConBultosAsync(confirmacion.EnvioId, ct)
            ?? throw new DomainException("El envío no existe.");

        // A1: una devolución abierta con bultos en devolución: el envío entero (EnDevolucion) o bultos sueltos mientras
        // otros del envío siguen su camino (RF 31).
        if (await envios.ObtenerDevolucionAsync(envio.Id, ct) is not { } devolucion
            || !envio.Bultos.Any(b => b.Estado == EstadoEnvio.EnDevolucion))
        {
            throw new DomainException($"El envío {envio.Numero} no está en devolución.");
        }

        var entregados = confirmacion.BultosEntregados.ToHashSet();
        if (entregados.Count == 0)
        {
            throw new DomainException("Marcá los bultos que se entregan al comercio.");
        }

        // RF 31: un bulto que ya recibió el destinatario, o que sigue en otro camino, no es parte de la devolución.
        var enDevolucion = BultosDeLaDevolucion(envio, devolucion).ToList();
        if (!entregados.IsSubsetOf(enDevolucion.Select(b => b.Id)))
        {
            throw new DomainException("Uno de los bultos marcados no es de esta devolución.");
        }

        var ahora = reloj.GetUtcNow();
        devolucion.Cerrar(confirmacion.NombreReceptor, confirmacion.DocumentoReceptor, ahora);
        var devolucionDelEnvio = envio.Estado == EstadoEnvio.EnDevolucion;
        if (devolucionDelEnvio)
        {
            envio.Transicionar(EstadoEnvio.Devuelto, OrigenEvento.Backoffice, confirmacion.ResponsableId, ahora,
                detalle: $"Lo recibió {devolucion.NombreReceptor} (documento {devolucion.DocumentoReceptor}) por el comercio.");
        }
        else
        {
            // Bultos sueltos: sólo los que recibió el comercio pasan a Devuelto; el envío y sus demás bultos no cambian.
            // Quién los recibió queda en la devolución.
            envio.DevolverBultos(entregados);
        }

        // A2: la entrega parcial queda registrada como incidencia del envío, con los códigos que faltaron.
        var faltantes = enDevolucion.Where(b => !entregados.Contains(b.Id)).Select(b => b.Codigo).Order().ToArray();
        if (faltantes.Length > 0)
        {
            envios.Agregar(Incidencia.Crear(envio, TipoIncidencia.Extravio,
                $"Devolución entregada al comercio sin los bultos: {string.Join(", ", faltantes)}.", ahora));
        }

        // RF 36: con bultos ya entregados el cierre es mixto; el evento lo indica para que los avisos v1 no lo reciban.
        // Una devolución de bultos sueltos no cierra el envío: sólo publica los eventos de bulto (avisos v2).
        if (devolucionDelEnvio)
        {
            eventos.Registrar(EnvioDevuelto.Tipo,
                new EnvioDevuelto(envio.Id, envio.Numero, ahora, confirmacion.ResponsableId, envio.BultosEntregados), ahora);
        }
        foreach (var bulto in enDevolucion.Where(b => entregados.Contains(b.Id)).OrderBy(b => b.Codigo))
        {
            eventos.Registrar(EventoBultoDevuelto.Tipo,
                new EventoBultoDevuelto(envio.Id, envio.Numero, bulto.Codigo, envio.Estado.ToString(), ahora), ahora);
        }
        await envios.GuardarCambiosAsync(ct);
    }
}

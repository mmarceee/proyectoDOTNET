using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Envios.Contracts.Results;
using Logistica.Modules.Envios.Domain.Devoluciones;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Domain.Incidencias;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Application;

// Implementación del contrato de Envíos. Es internal: los otros módulos sólo ven la interfaz.
internal sealed class EnviosModuleApi(IEnvioRepository envios, TimeProvider reloj, IReglasDeEntrega reglasDeEntrega,
    IIntegrationEventPublisher eventos) : IEnviosModuleApi
{
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

    // CU-17 (T6). Las reglas de negocio no lanzan hacia afuera: vuelven como resultado, para que Ejecución aplique la
    // política de conflictos (ADR-0004, sección 2.4). Los archivos se agregan recién cuando el dominio aceptó la entrega.
    public async Task<RegistroEnCalle> RegistrarEntregaAsync(EntregaARegistrar entrega, CancellationToken ct)
    {
        var envio = await envios.ObtenerConIntentosAsync(entrega.EnvioId, ct);
        if (envio is null)
        {
            return new(ResultadoRegistro.EnvioNoEncontrado);
        }

        try
        {
            var reglas = await reglasDeEntrega.DelEnvioAsync(envio, ct);
            var firma = ArchivoEvidencia.Crear(envio, entrega.Firma.TipoContenido, entrega.Firma.Contenido);
            var foto = entrega.Foto is null ? null : ArchivoEvidencia.Crear(envio, entrega.Foto.TipoContenido, entrega.Foto.Contenido);
            var prueba = new PruebaEntrega(firma.Id, foto?.Id, entrega.NombreReceptor, entrega.DocumentoReceptor,
                new Ubicacion(entrega.Latitud, entrega.Longitud), entrega.OcurridoEn);

            envio.RegistrarEntrega(prueba, reglas, entrega.ResponsableId, entrega.OcurridoEn);

            envios.Agregar(firma);
            if (foto is not null)
            {
                envios.Agregar(foto);
            }

            var entregadoEn = envio.Eventos[^1].OcurridoEn;
            eventos.Registrar(EnvioEntregado.Tipo,
                new EnvioEntregado(envio.Id, envio.Numero, entregadoEn, entrega.ResponsableId), entregadoEn);

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

    // CU-18 (T7 con T9 o T10). Si el envío queda EnDevolucion, inicia la Devolucion en la misma transacción (CU-20).
    public async Task<RegistroEnCalle> RegistrarIntentoFallidoAsync(IntentoFallidoARegistrar intento, CancellationToken ct)
    {
        var envio = await envios.ObtenerConIntentosAsync(intento.EnvioId, ct);
        if (envio is null)
        {
            return new(ResultadoRegistro.EnvioNoEncontrado);
        }

        try
        {
            var reglas = await reglasDeEntrega.DelEnvioAsync(envio, ct);
            var motivo = await reglasDeEntrega.MotivoAsync(envio, intento.MotivoId, ct)
                ?? throw new DomainException("El motivo no está en el catálogo de las reglas del envío.");
            var ubicacion = new Ubicacion(intento.Latitud, intento.Longitud);
            var foto = intento.Foto is null ? null : ArchivoEvidencia.Crear(envio, intento.Foto.TipoContenido, intento.Foto.Contenido);
            var evidencia = foto is null ? null : new PruebaEntrega(null, foto.Id, null, null, ubicacion, intento.OcurridoEn);

            var registrado = envio.RegistrarIntentoFallido(motivo, evidencia, intento.Observaciones, ubicacion, reglas,
                intento.ResponsableId, intento.OcurridoEn);

            if (foto is not null)
            {
                envios.Agregar(foto);
            }

            if (envio.Estado == EstadoEnvio.EnDevolucion)
            {
                envios.Agregar(Devolucion.Crear(envio, $"Intento fallido: {motivo.Codigo}.", intento.OcurridoEn));
            }

            eventos.Registrar(IntentoFallidoRegistrado.Tipo,
                new IntentoFallidoRegistrado(envio.Id, envio.Numero, registrado.NumeroIntento, motivo.Codigo,
                    envio.Estado.ToString(), registrado.FechaHora, envio.ReintentoDesde),
                registrado.FechaHora);

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
            e.ReintentoDesde)).ToList();
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

    public async Task RecibirEnDepositoAsync(Guid envioId, Guid? responsableId, CancellationToken ct)
    {
        var envio = await envios.ObtenerAsync(envioId, ct)
            ?? throw new DomainException("El envío no existe.");

        // T2. Si el envío ya no está Admitido (por ejemplo, lo cancelaron), Transicionar lo rechaza.
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, responsableId, reloj.GetUtcNow());

        await envios.GuardarCambiosAsync(ct);
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
}

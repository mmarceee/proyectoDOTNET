namespace Logistica.Modules.Envios.Domain.Envios;

// Acceso a los envíos guardados. Lo implementa Infrastructure, así Application no depende de EF Core.
// Sólo devuelve envíos del inquilino actual: los de otro operador son como si no existieran.
internal interface IEnvioRepository
{
    Task<string> SiguienteNumeroAsync(CancellationToken ct);

    // Con sus bultos. El código del bulto es único dentro del operador.
    Task<Envio?> ObtenerPorCodigoBultoAsync(string codigoBulto, CancellationToken ct);
    Task<IReadOnlyList<Bulto>> ObtenerBultosPorCodigosAsync(IReadOnlyCollection<string> codigos, CancellationToken ct);

    Task<Envio?> ObtenerAsync(Guid id, CancellationToken ct);

    // Con sus intentos: CU-17 y CU-18 numeran el intento nuevo y cuentan los fallidos.
    Task<Envio?> ObtenerConIntentosAsync(Guid id, CancellationToken ct);

    void Agregar(Envio envio);

    // La firma y la foto de una entrega o un intento (CU-17 y CU-18), y la devolución que inicia T10 (CU-18).
    void Agregar(ArchivoEvidencia archivo);
    void Agregar(Devoluciones.Devolucion devolucion);

    // La incidencia de los bultos que no volvieron en una rendición (CU-55, A1).
    void Agregar(Incidencias.Incidencia incidencia);

    // CU-55: los envíos, con sus bultos, a los que pertenecen estos bultos.
    Task<IReadOnlyList<Envio>> ObtenerPorBultosAsync(IReadOnlyCollection<Guid> bultoIds, CancellationToken ct);
    Task<IReadOnlyList<Envio>> ObtenerParaPlanificacionAsync(IReadOnlyCollection<Guid>? envioIds, CancellationToken ct);

    // CU-04: ¿hay envíos con entrega pendiente en alguna de estas franjas, en esa fecha o después?
    Task<bool> HayComprometidosAsync(IReadOnlyCollection<Guid> franjaIds, DateOnly desde, CancellationToken ct);

    // CU-04: los envíos con entrega pendiente en alguna de estas franjas, pero sin fecha propia.
    Task<IReadOnlyList<Guid>> SinFechaConFranjasAsync(IReadOnlyCollection<Guid> franjaIds, CancellationToken ct);

    // CU-60: el envío con sus eventos, sólo para leer.
    Task<Envio?> ObtenerConEventosAsync(Guid id, CancellationToken ct);

    // CU-60: el Id de un envío por su número, sin traer el envío.
    Task<Guid?> IdPorNumeroAsync(string numero, CancellationToken ct);
// Los metadatos del sobre se toman de la sesión al registrar el evento.
    void RegistrarAsignacion(Guid envioId, Guid rutaId, Guid? responsableId, DateTimeOffset ahora);
    void RegistrarDesasignacion(Guid envioId, Guid rutaId, Guid? responsableId, DateTimeOffset ahora);

    // CU-16: deja EnvioCancelado en el Outbox; se guarda junto con el cambio de estado en GuardarCambiosAsync.
    void RegistrarCancelacion(Envio envio, string? motivo, DateTimeOffset ahora);

    // CU-19: deja EnvioReprogramado en el Outbox; se guarda junto con la nueva fecha en GuardarCambiosAsync.
    // CU-61: el mismo evento, con origen en el seguimiento público.
    void RegistrarReprogramacion(Envio envio, DateTimeOffset ahora, OrigenEvento origen = OrigenEvento.Backoffice);

    // CU-21: deja EnvioExtraviado en el Outbox; se guarda junto con el cambio de estado y la incidencia en GuardarCambiosAsync.
    void RegistrarExtravio(Envio envio, EstadoEnvio estadoAnterior, string motivo, Guid? responsableId, DateTimeOffset ahora);

    // CU-20: deja DevolucionIniciada en el Outbox; se guarda junto con el cambio de estado y la devolución en GuardarCambiosAsync.
    void RegistrarDevolucion(Envio envio, Devoluciones.Devolucion devolucion, EstadoEnvio estadoAnterior, Guid? responsableId,
        OrigenEvento origen, DateTimeOffset ahora);

    // CU-66: los envíos de esas rutas y los pendientes de entrega con fecha hasta "hasta", sin seguimiento de cambios.
    Task<IReadOnlyList<Envio>> ListarParaTableroAsync(IReadOnlyCollection<Guid> ids, DateOnly hasta, CancellationToken ct);

    Task GuardarCambiosAsync(CancellationToken ct);
}

namespace Logistica.Http.Contracts.Repartidor;

// Cuerpo de POST /api/repartidor/sincronizacion (CU-52; ADR-0004, sección 2.1): las operaciones que el repartidor
// registró sin conexión, en el orden en que ocurrieron (Secuencia), hasta 20 por lote.
public sealed record SincronizacionRequest(IReadOnlyList<OperacionDispositivoRequest> Operaciones, int? Pendientes = null);

// Una operación de la PWA. OperacionId lo genera el dispositivo al confirmarla y la hace idempotente. La hora y la
// posición son las del dispositivo. Según el Tipo, viaja Visita, Entrega, IntentoFallido, Carga o Rendicion.
public sealed record OperacionDispositivoRequest(
    Guid OperacionId,
    Guid DispositivoId,
    long Secuencia,
    string Tipo,
    Guid RutaId,
    Guid? EnvioId,
    DateTimeOffset OcurridoEn,
    decimal? Latitud,
    decimal? Longitud,
    string? CorrelacionId = null,
    EntregaRequest? Entrega = null,
    IntentoFallidoRequest? IntentoFallido = null,
    CargaRequest? Carga = null,
    IniciarRendicionRequest? Rendicion = null,
    VisitaRequest? Visita = null);

// Tipo "RegistrarVisita" (CU-17 y CU-18 por bulto, RF 32; ADR-0004, sección 2.11): el resultado de cada bulto del envío
// que el repartidor tenía en la visita. Sin motivo, el bulto se entregó; con motivo, del catálogo de las reglas del envío,
// no se pudo entregar. La firma, obligatoria si se entregó alguno, respalda los bultos entregados.
public sealed record VisitaRequest(IReadOnlyList<ResultadoBultoRequest> Bultos, ArchivoRequest? Firma = null,
    ArchivoRequest? Foto = null, string? NombreReceptor = null, string? DocumentoReceptor = null, string? Observaciones = null);
public sealed record ResultadoBultoRequest(string CodigoBulto, Guid? MotivoId = null);

public sealed record CargaRequest(long RevisionRuta, IReadOnlyList<LecturaCargaRequest> Lecturas, bool AceptaFaltantes = false);
public sealed record LecturaCargaRequest(string CodigoBulto, DateTimeOffset EscaneadoEn);

// Tipo "RegistrarEntrega" (CU-17), anterior a la entrega parcial: el servidor la traduce a una visita que entrega todos
// los bultos del envío (ADR-0004, sección 2.11). La firma es siempre obligatoria.
public sealed record EntregaRequest(ArchivoRequest Firma, ArchivoRequest? Foto = null,
    string? NombreReceptor = null, string? DocumentoReceptor = null);

// Tipo "RegistrarIntentoFallido" (CU-18), anterior a la entrega parcial: se traduce a una visita con el mismo motivo para
// todos los bultos del envío. El motivo es del catálogo de las reglas del envío.
public sealed record IntentoFallidoRequest(Guid MotivoId, ArchivoRequest? Foto = null, string? Observaciones = null);

// Firma o foto, ya comprimida en el dispositivo: PNG o JPEG, hasta 1 MB.
public sealed record ArchivoRequest(string TipoContenido, string ContenidoBase64);

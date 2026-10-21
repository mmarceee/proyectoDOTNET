namespace Logistica.Http.Contracts.Repartidor;

// Cuerpo de POST /api/repartidor/sincronizacion (CU-52; ADR-0004, sección 2.1): las operaciones que el repartidor
// registró sin conexión, en el orden en que ocurrieron (Secuencia), hasta 20 por lote.
public sealed record SincronizacionRequest(IReadOnlyList<OperacionDispositivoRequest> Operaciones, int? Pendientes = null);

// Una operación de la PWA. OperacionId lo genera el dispositivo al confirmarla y la hace idempotente. La hora y la
// posición son las del dispositivo. Según el Tipo, viaja Entrega o IntentoFallido.
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
    IniciarRendicionRequest? Rendicion = null);

public sealed record CargaRequest(long RevisionRuta, IReadOnlyList<LecturaCargaRequest> Lecturas, bool AceptaFaltantes = false);
public sealed record LecturaCargaRequest(string CodigoBulto, DateTimeOffset EscaneadoEn);

// Tipo "RegistrarEntrega" (CU-17). La firma es siempre obligatoria.
public sealed record EntregaRequest(ArchivoRequest Firma, ArchivoRequest? Foto = null,
    string? NombreReceptor = null, string? DocumentoReceptor = null);

// Tipo "RegistrarIntentoFallido" (CU-18). El motivo es del catálogo de las reglas del envío.
public sealed record IntentoFallidoRequest(Guid MotivoId, ArchivoRequest? Foto = null, string? Observaciones = null);

// Firma o foto, ya comprimida en el dispositivo: PNG o JPEG, hasta 1 MB.
public sealed record ArchivoRequest(string TipoContenido, string ContenidoBase64);

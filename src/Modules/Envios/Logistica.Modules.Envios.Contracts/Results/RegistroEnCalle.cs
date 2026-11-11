namespace Logistica.Modules.Envios.Contracts.Results;

// Un archivo de evidencia tal como llega del dispositivo (CU-17 y CU-18): PNG o JPEG, hasta 1 MB.
public sealed record ArchivoRecibido(string TipoContenido, byte[] Contenido);

// Los bultos de un envío con su estado actual, para que Ejecución clasifique cada bulto de una visita (ADR-0004,
// sección 2.11). AdmiteEntregaParcial es la regla de la versión de reglas del envío (RF 33).
public sealed record EnvioParaVisita(Guid EnvioId, IReadOnlyList<BultoParaVisita> Bultos, bool AdmiteEntregaParcial);
public sealed record BultoParaVisita(Guid BultoId, string Codigo, string Estado);

// Lo que registró el repartidor en una visita (CU-17 y CU-18 por bulto). La hora y la posición son las del dispositivo.
// Un resultado sin motivo es una entrega; con motivo, del catálogo de la versión de reglas, un intento fallido. La firma
// respalda los bultos entregados; la foto acompaña la entrega o el intento.
public sealed record VisitaARegistrar(Guid EnvioId, DateTimeOffset OcurridoEn, decimal Latitud, decimal Longitud,
    IReadOnlyList<ResultadoBultoARegistrar> Resultados, ArchivoRecibido? Firma, ArchivoRecibido? Foto,
    string? NombreReceptor, string? DocumentoReceptor, string? Observaciones, Guid? ResponsableId,
    Guid? RepartidorId = null, Guid? RutaId = null);
public sealed record ResultadoBultoARegistrar(Guid BultoId, Guid? MotivoId);

// Cómo terminó el registro en Envíos. Conflicto: el estado actual del envío no admite la operación (ADR-0004,
// sección 2.3, paso 3); EstadoActual dice cuál es. Invalido: los datos no cumplen las reglas del envío (paso 2).
public enum ResultadoRegistro { Aplicado, Conflicto, Invalido, EnvioNoEncontrado }

public sealed record RegistroEnCalle(ResultadoRegistro Resultado, string? EstadoActual = null, string? Detalle = null);

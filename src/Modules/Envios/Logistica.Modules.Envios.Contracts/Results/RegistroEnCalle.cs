namespace Logistica.Modules.Envios.Contracts.Results;

// Un archivo de evidencia tal como llega del dispositivo (CU-17 y CU-18): PNG o JPEG, hasta 1 MB.
public sealed record ArchivoRecibido(string TipoContenido, byte[] Contenido);

// Lo que registró el repartidor al entregar (CU-17). La hora y la posición son las del dispositivo.
public sealed record EntregaARegistrar(Guid EnvioId, DateTimeOffset OcurridoEn, decimal Latitud, decimal Longitud,
    ArchivoRecibido Firma, ArchivoRecibido? Foto, string? NombreReceptor, string? DocumentoReceptor, Guid? ResponsableId);

// Lo que registró el repartidor al no poder entregar (CU-18). El motivo es del catálogo de la versión de reglas.
public sealed record IntentoFallidoARegistrar(Guid EnvioId, DateTimeOffset OcurridoEn, decimal Latitud, decimal Longitud,
    Guid MotivoId, ArchivoRecibido? Foto, string? Observaciones, Guid? ResponsableId);

// Cómo terminó el registro en Envíos. Conflicto: el estado actual del envío no admite la operación (ADR-0004,
// sección 2.3, paso 3); EstadoActual dice cuál es. Invalido: los datos no cumplen las reglas del envío (paso 2).
public enum ResultadoRegistro { Aplicado, Conflicto, Invalido, EnvioNoEncontrado }

public sealed record RegistroEnCalle(ResultadoRegistro Resultado, string? EstadoActual = null, string? Detalle = null);

namespace Logistica.Http.Contracts.Seguimiento;

// CU-66: la foto completa del tablero. La página la pide al abrir y al reconectarse (A1); después, SignalR manda las
// novedades.
public sealed record TableroResponse(DateTimeOffset GeneradoEn, IReadOnlyList<RutaTableroResponse> Rutas,
    IReadOnlyList<EnvioEnRiesgoResponse> EnRiesgo, IReadOnlyList<ConflictoTableroResponse> Conflictos);

// Un conflicto de sincronización pendiente de revisión (ADR-0004, sección 2.5). Tipo es la operación de la PWA
// (RegistrarEntrega, RegistrarIntentoFallido...); el envío, si se conoce.
public sealed record ConflictoTableroResponse(Guid OperacionId, string Repartidor, string Tipo, string Resultado,
    string? EnvioNumero, string? Motivo, DateTimeOffset OcurridoEn);

// Una novedad en tiempo real (paso 3): una entrega, un intento fallido o una alerta. Al recibirla, la página la muestra
// y vuelve a pedir la foto, así los contadores y los riesgos quedan al día.
public sealed record NovedadTableroResponse(string Tipo, string Texto, bool Alerta, DateTimeOffset OcurridoEn);

// Una ruta en la calle: quién, con qué vehículo, cuánto avanzó y dónde está.
public sealed record RutaTableroResponse(Guid RutaId, string Estado, string Repartidor, string Vehiculo, Guid VehiculoId,
    int Envios, int Entregados, int Pendientes, PosicionTableroResponse? Posicion);

// La última posición conocida del vehículo (CU-53). Se compara por CapturadaEn, nunca por la hora en que llegó.
public sealed record PosicionTableroResponse(Guid Id, Guid VehiculoId, Guid RutaId, decimal Latitud, decimal Longitud,
    DateTimeOffset CapturadaEn);

// Un envío no entregado con menos de 60 minutos hasta el fin de su ventana, o con la ventana vencida.
public sealed record EnvioEnRiesgoResponse(string Numero, string Estado, DateTimeOffset FinVentana, int MinutosRestantes,
    bool Vencido);

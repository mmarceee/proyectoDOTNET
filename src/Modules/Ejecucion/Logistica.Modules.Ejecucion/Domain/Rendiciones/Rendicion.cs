using Logistica.SharedKernel;

namespace Logistica.Modules.Ejecucion.Domain.Rendiciones;

internal enum EstadoLineaRendicion { EntregadoEnCalle, DevueltoADeposito, Extraviado }

internal sealed class Rendicion : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid RutaId { get; private set; }
    public Guid RepartidorId { get; private set; }
    public Guid OperacionId { get; private set; }
    public DateTimeOffset IniciadaEn { get; private set; }
    public DateTimeOffset? FinalizadaEn { get; private set; }
    public string? Observaciones { get; private set; }
    private readonly List<LineaRendicion> _lineas = [];
    public IReadOnlyList<LineaRendicion> Lineas => _lineas;
    private Rendicion() { }

    public static Rendicion Iniciar(Guid operador, Guid ruta, Guid repartidor, Guid operacion,
        DateTimeOffset fecha, string? observaciones, IReadOnlyList<DeclaracionRendicion> lineas)
    {
        if (new[] { operador, ruta, repartidor, operacion }.Any(id => id == Guid.Empty)
            || fecha == default || observaciones?.Length > 2000 || lineas.Count > 2000
            || lineas.Select(l => l.BultoId).Distinct().Count() != lineas.Count)
            throw new DomainException("La declaración de rendición no es válida.");
        var rendicion = new Rendicion { OperadorId = operador, RutaId = ruta, RepartidorId = repartidor,
            OperacionId = operacion, IniciadaEn = fecha.ToUniversalTime(), Observaciones = Limpiar(observaciones) };
        foreach (var linea in lineas) rendicion._lineas.Add(LineaRendicion.Crear(rendicion, linea));
        return rendicion;
    }
    internal static string? Limpiar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}

internal sealed record DeclaracionRendicion(Guid EnvioId, Guid BultoId, string Situacion, bool Declarada, string? Observacion);

internal sealed class LineaRendicion : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid RendicionId { get; private set; }
    public Guid EnvioId { get; private set; }
    public Guid BultoId { get; private set; }
    public string Situacion { get; private set; } = "";
    public bool Declarada { get; private set; }
    public string? Observacion { get; private set; }
    public bool? Recibida { get; private set; }
    public EstadoLineaRendicion? EstadoFinal { get; private set; }
    private LineaRendicion() { }

    internal static LineaRendicion Crear(Rendicion rendicion, DeclaracionRendicion linea)
    {
        var requerida = linea.Situacion is "NoIntentado" or "IntentoFallido" or "CargaParcial";
        if (linea.EnvioId == Guid.Empty || linea.BultoId == Guid.Empty || linea.Observacion?.Length > 1000
            || linea.Situacion is not ("NoIntentado" or "IntentoFallido" or "CargaParcial" or "NoCargado" or "EntregadoEnCalle" or "Resuelto" or "Extraviado")
            || requerida && !linea.Declarada && string.IsNullOrWhiteSpace(linea.Observacion)
            || !requerida && linea.Declarada)
            throw new DomainException("Indicá qué bultos devolvés y explicá los que no vuelven.");
        return new() { OperadorId = rendicion.OperadorId, RendicionId = rendicion.Id, EnvioId = linea.EnvioId,
            BultoId = linea.BultoId, Situacion = linea.Situacion, Declarada = linea.Declarada,
            Observacion = Rendicion.Limpiar(linea.Observacion) };
    }
}

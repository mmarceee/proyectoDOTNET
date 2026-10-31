using Logistica.SharedKernel;

namespace Logistica.BuildingBlocks.Infrastructure.Messaging.Fallidos;

public enum EstadoMensajeFallido
{
    Pendiente,
    Reintentado,
    Descartado
}

// Copia de un mensaje de logistica.fallidos (ADR-0003, sección 2.6): el sobre completo, la cola de origen, los intentos
// y el historial de errores. Es información del operador: lleva el OperadorId del sobre y el filtro de inquilino, así
// cada administrador ve sólo los suyos (CU-09, requerimiento 1).
public sealed class MensajeFallido : Entity, IOperadorOwned
{
    public const int LargoError = 200;
    public const int LargoMotivo = 500;

    private readonly List<string> _historial = [];

    public Guid OperadorId { get; private set; }
    public Guid? ComercioId { get; private set; }
    public Guid MessageId { get; private set; }
    public string ColaOrigen { get; private set; } = "";
    public string Tipo { get; private set; } = "";
    public DateTimeOffset OcurridoEn { get; private set; }
    public string? TraceParent { get; private set; }
    public string Contenido { get; private set; } = "";
    public int Intentos { get; private set; }
    public string? UltimoError { get; private set; }
    public IReadOnlyList<string> Historial => _historial.AsReadOnly();
    public EstadoMensajeFallido Estado { get; private set; }
    public DateTimeOffset RegistradoEn { get; private set; }
    public DateTimeOffset ActualizadoEn { get; private set; }
    // Quién lo reintentó o lo descartó, y cuándo (CU-09, A1).
    public string? ResueltoPor { get; private set; }
    public DateTimeOffset? ResueltoEn { get; private set; }
    public string? MotivoDescarte { get; private set; }

    private MensajeFallido() { }

    public static MensajeFallido Registrar(MensajeIntegracion mensaje, string colaOrigen, int intentos, string? error,
        DateTimeOffset ahora)
    {
        var fallido = new MensajeFallido
        {
            OperadorId = mensaje.OperadorId,
            ComercioId = mensaje.ComercioId,
            MessageId = mensaje.Id,
            ColaOrigen = colaOrigen,
            Tipo = mensaje.Tipo,
            OcurridoEn = mensaje.OcurridoEn,
            TraceParent = mensaje.TraceParent,
            Contenido = mensaje.Contenido,
            RegistradoEn = ahora,
        };
        fallido.VolvioAFallar(intentos, error, ahora);
        return fallido;
    }

    // A3: el mensaje reintentado agotó otra vez sus reintentos. Vuelve a la lista con el error nuevo en su historial.
    public void VolvioAFallar(int intentos, string? error, DateTimeOffset ahora)
    {
        Intentos = intentos;
        UltimoError = error is { Length: > LargoError } ? error[..LargoError] : error;
        Estado = EstadoMensajeFallido.Pendiente;
        ActualizadoEn = ahora;
        ResueltoPor = null;
        ResueltoEn = null;
        MotivoDescarte = null;
        _historial.Add($"{ahora:yyyy-MM-dd HH:mm:ss} · {intentos} intentos · {UltimoError ?? "sin detalle"}");
    }

    // El sobre tal como llegó: se vuelve a publicar con el mismo MessageId (CU-09, requerimiento 2).
    public MensajeIntegracion Sobre() => new(MessageId, Tipo, OperadorId, ComercioId, OcurridoEn, TraceParent, Contenido);

    public void MarcarReintentado(string usuario, DateTimeOffset ahora)
    {
        ExigirPendiente();
        Resolver(EstadoMensajeFallido.Reintentado, usuario, ahora);
    }

    public void Descartar(string usuario, string motivo, DateTimeOffset ahora)
    {
        ExigirPendiente();
        if (string.IsNullOrWhiteSpace(motivo)) throw new DomainException("Indicá el motivo del descarte.");
        var limpio = motivo.Trim();
        MotivoDescarte = limpio.Length > LargoMotivo ? limpio[..LargoMotivo] : limpio;
        Resolver(EstadoMensajeFallido.Descartado, usuario, ahora);
    }

    private void Resolver(EstadoMensajeFallido estado, string usuario, DateTimeOffset ahora)
    {
        Estado = estado;
        ResueltoPor = usuario;
        ResueltoEn = ahora;
        ActualizadoEn = ahora;
    }

    private void ExigirPendiente()
    {
        if (Estado != EstadoMensajeFallido.Pendiente) throw new DomainException("El mensaje ya se reintentó o se descartó.");
    }
}

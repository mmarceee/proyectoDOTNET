using Logistica.SharedKernel;

namespace Logistica.Modules.Ejecucion.Domain.Operaciones;

internal enum ResultadoOperacion { Aplicada, Conflicto, Invalida }

// Una operación que la PWA sincronizó (ADR-0004, sección 2.2), con su resultado. El Id es el OperacionId que generó el
// dispositivo: si la PWA la reenvía, se reconoce y no se vuelve a aplicar. Se guarda en la misma transacción que el
// efecto. Una operación rechazada guarda su contenido completo, con la evidencia, sin aplicarla (sección 2.5).
internal sealed class OperacionDispositivo : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid RepartidorId { get; private set; }
    public Guid DispositivoId { get; private set; }
    public long Secuencia { get; private set; }
    public string Tipo { get; private set; } = "";
    public Guid RutaId { get; private set; }
    public Guid? EnvioId { get; private set; }

    // Hora del dispositivo, en UTC. Si llegó adelantada, es la de recepción y HoraCorregida queda en true (sección 2.7).
    public DateTimeOffset OcurridoEn { get; private set; }
    public DateTimeOffset RecibidoEn { get; private set; }
    public bool HoraCorregida { get; private set; }

    public ResultadoOperacion Resultado { get; private set; }
    public string? Motivo { get; private set; }
    public string? Detalle { get; private set; }

    // Sólo en las rechazadas: la operación tal como llegó, en JSON.
    public string? Contenido { get; private set; }

    // Un conflicto queda pendiente hasta que el despachador lo revisa (tablero, CU-66).
    public bool Revisada { get; private set; }

    private OperacionDispositivo() { } // para EF Core

    private OperacionDispositivo(Guid operacionId) : base(operacionId) { }

    public static OperacionDispositivo Aplicada(Guid operacionId, DatosOperacion datos)
    {
        return Crear(operacionId, datos, ResultadoOperacion.Aplicada, null, null, null);
    }

    public static OperacionDispositivo Rechazada(Guid operacionId, DatosOperacion datos, ResultadoOperacion resultado,
        string motivo, string? detalle, string contenido)
    {
        if (resultado == ResultadoOperacion.Aplicada || string.IsNullOrWhiteSpace(motivo))
        {
            throw new DomainException("Una operación rechazada necesita un conflicto o una invalidez, con su motivo.");
        }

        return Crear(operacionId, datos, resultado, motivo, detalle, contenido);
    }

    private static OperacionDispositivo Crear(Guid operacionId, DatosOperacion datos, ResultadoOperacion resultado,
        string? motivo, string? detalle, string? contenido)
    {
        if (operacionId == Guid.Empty || datos.OperadorId == Guid.Empty || datos.RepartidorId == Guid.Empty)
        {
            throw new DomainException("La operación necesita su identificador, el operador y el repartidor.");
        }

        return new OperacionDispositivo(operacionId)
        {
            OperadorId = datos.OperadorId,
            RepartidorId = datos.RepartidorId,
            DispositivoId = datos.DispositivoId,
            Secuencia = datos.Secuencia,
            Tipo = Recortar(datos.Tipo, 40)!,
            RutaId = datos.RutaId,
            EnvioId = datos.EnvioId,
            OcurridoEn = datos.OcurridoEn.ToUniversalTime(),
            RecibidoEn = datos.RecibidoEn.ToUniversalTime(),
            HoraCorregida = datos.HoraCorregida,
            Resultado = resultado,
            Motivo = motivo,
            Detalle = Recortar(detalle, 500),
            Contenido = contenido,
        };
    }

    // El tipo y el detalle vienen del dispositivo o de un mensaje de error: se recortan al tamaño de su columna, para
    // que una operación inválida igual se pueda registrar.
    private static string? Recortar(string? texto, int largo)
    {
        return texto is { Length: var n } && n > largo ? texto[..largo] : texto;
    }
}

// Lo común a toda operación, aplicada o rechazada.
internal sealed record DatosOperacion(Guid OperadorId, Guid RepartidorId, Guid DispositivoId, long Secuencia, string Tipo,
    Guid RutaId, Guid? EnvioId, DateTimeOffset OcurridoEn, DateTimeOffset RecibidoEn, bool HoraCorregida);

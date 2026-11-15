using System.Security.Cryptography;
using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Domain.Reprogramaciones;

// Cómo se comprueba que quien reprograma es el destinatario (CU-61, requerimiento especial 4): un código que le llega al
// correo o, si el envío no tiene correo, los últimos cuatro dígitos de su teléfono.
internal enum MedioVerificacion
{
    Correo,
    Telefono
}

internal enum ResultadoVerificacion
{
    Valida,
    Incorrecta,
    Vencida,
    Agotada,
    Usada,
    OtraOpcion
}

// CU-61: el enlace de seguimiento no alcanza para reprogramar, porque cualquiera que lo reciba podría cambiar la entrega.
// Antes de reprogramar, el destinatario confirma con un secreto que sólo él tiene. Esta verificación vale para una
// fecha y franja, vence a los 10 minutos, admite 3 intentos y se usa una sola vez. Del secreto se guarda sólo su hash.
internal sealed class VerificacionReprogramacion : Entity, IOperadorOwned
{
    public const int MaxIntentos = 3;
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(10);

    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public DateOnly Fecha { get; private set; }
    public Guid FranjaId { get; private set; }
    public MedioVerificacion Medio { get; private set; }
    public byte[] HashSecreto { get; private set; } = [];
    public DateTimeOffset CreadaEn { get; private set; }
    public DateTimeOffset VenceEn { get; private set; }
    public int Intentos { get; private set; }
    public DateTimeOffset? UsadaEn { get; private set; }
    // Pedir otra verificación anula la anterior: sólo vale la última.
    public bool Anulada { get; private set; }

    public int IntentosRestantes => Math.Max(0, MaxIntentos - Intentos);

    private VerificacionReprogramacion() { }

    public static VerificacionReprogramacion Crear(Guid operadorId, Guid envioId, DateOnly fecha, Guid franjaId,
        MedioVerificacion medio, byte[] hashSecreto, DateTimeOffset ahora)
    {
        if (operadorId == Guid.Empty || envioId == Guid.Empty || franjaId == Guid.Empty)
            throw new DomainException("La verificación necesita el operador, el envío y la franja.");
        if (hashSecreto is not { Length: > 0 })
            throw new DomainException("La verificación necesita el secreto.");

        return new()
        {
            OperadorId = operadorId,
            EnvioId = envioId,
            Fecha = fecha,
            FranjaId = franjaId,
            Medio = medio,
            HashSecreto = hashSecreto,
            CreadaEn = ahora,
            VenceEn = ahora + Vigencia,
        };
    }

    // Un secreto incorrecto consume un intento; pedir otra fecha con el mismo código no, porque no es adivinar el
    // secreto. No la marca usada: eso pasa recién cuando la reprogramación se aplicó (MarcarUsada).
    public ResultadoVerificacion Comprobar(DateOnly fecha, Guid franjaId, byte[] hash, DateTimeOffset ahora)
    {
        if (UsadaEn is not null) return ResultadoVerificacion.Usada;
        if (Anulada || ahora >= VenceEn) return ResultadoVerificacion.Vencida;
        if (Intentos >= MaxIntentos) return ResultadoVerificacion.Agotada;
        if (fecha != Fecha || franjaId != FranjaId) return ResultadoVerificacion.OtraOpcion;

        // Comparación de tiempo constante, como la firma del token: el tiempo de respuesta no da pistas.
        if (!CryptographicOperations.FixedTimeEquals(hash, HashSecreto))
        {
            Intentos++;
            return Intentos >= MaxIntentos ? ResultadoVerificacion.Agotada : ResultadoVerificacion.Incorrecta;
        }

        return ResultadoVerificacion.Valida;
    }

    public void MarcarUsada(DateTimeOffset ahora)
    {
        if (UsadaEn is not null) throw new DomainException("La verificación ya se usó.");
        UsadaEn = ahora;
    }

    public void Anular() => Anulada = true;
}

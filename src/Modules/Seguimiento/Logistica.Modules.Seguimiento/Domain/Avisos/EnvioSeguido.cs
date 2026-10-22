using Logistica.SharedKernel;

namespace Logistica.Modules.Seguimiento.Domain.Avisos;

// Copia mínima de un envío en Seguimiento (CU-64 y CU-62). Se crea con EnvioAdmitido, cuyo sobre trae el comercio porque
// se publica desde su sesión. Los eventos posteriores los publican el operador o el repartidor, sin comercio en el sobre:
// esta copia dice a qué comercio avisar sin leer el inquilino del contenido de esos eventos (ADR-0003, sección 2.2), y
// a quién notificar, porque el Worker no consulta Envíos.
internal sealed class EnvioSeguido : Entity, IOperadorOwned, IComercioOwned
{
    public const int LargoNombre = 200;
    public const int LargoEmail = 320;

    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public string Numero { get; private set; } = "";
    public string? NombreDestinatario { get; private set; }
    // Sin correo, las notificaciones quedan NoEnviable (CU-62, A2).
    public string? EmailDestinatario { get; private set; }

    private EnvioSeguido() { }

    // El Id es el del envío en Envíos: lo referencia por identificador, sin depender de su dominio.
    private EnvioSeguido(Guid envioId) : base(envioId) { }

    public static EnvioSeguido Crear(Guid operadorId, Guid comercioId, Guid envioId, string numero,
        string? nombreDestinatario = null, string? emailDestinatario = null)
    {
        if (operadorId == Guid.Empty || comercioId == Guid.Empty || envioId == Guid.Empty || string.IsNullOrWhiteSpace(numero))
            throw new DomainException("El envío seguido necesita operador, comercio, identificador y número.");
        return new(envioId)
        {
            OperadorId = operadorId,
            ComercioId = comercioId,
            Numero = numero,
            NombreDestinatario = Recortar(nombreDestinatario, LargoNombre),
            EmailDestinatario = Recortar(emailDestinatario, LargoEmail),
        };
    }

    private static string? Recortar(string? valor, int largo)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var limpio = valor.Trim();
        return limpio.Length > largo ? limpio[..largo] : limpio;
    }
}

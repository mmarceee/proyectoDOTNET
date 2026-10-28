using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Devoluciones;

internal enum EstadoDevolucion { Pendiente, EnTransitoADeposito, RecibidaEnDeposito, Cerrada }

// Agregado independiente; los CU-20/31 coordinarán sus cambios con los del envío.
internal sealed class Devolucion : Entity, IOperadorOwned, IComercioOwned
{
    public Guid EnvioId { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public string Motivo { get; private set; } = "";
    public EstadoDevolucion Estado { get; private set; }
    public DateTimeOffset IniciadaEn { get; private set; }
    public DateTimeOffset? RecibidaEnDepositoEn { get; private set; }
    public string? NombreReceptor { get; private set; }
    public string? DocumentoReceptor { get; private set; }
    public DateTimeOffset? EntregadaAlComercioEn { get; private set; }

    private Devolucion() { }

    public static Devolucion Crear(Envio envio, string motivo, DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new DomainException("La devolución necesita un motivo.");
        }

        return new Devolucion
        {
            EnvioId = envio.Id, OperadorId = envio.OperadorId, ComercioId = envio.ComercioId,
            Motivo = motivo.Trim(), Estado = EstadoDevolucion.Pendiente,
            // En UTC: CU-18 la inicia con la hora del dispositivo, que trae su desfase local.
            IniciadaEn = ahora.ToUniversalTime(),
        };
    }

    // CU-55: los bultos del envío volvieron al depósito en la rendición. Repetirlo no cambia nada.
    public void MarcarRecibida(DateTimeOffset ahora)
    {
        if (Estado == EstadoDevolucion.Cerrada)
        {
            throw new DomainException("La devolución ya se entregó al comercio.");
        }

        if (RecibidaEnDepositoEn is not null)
        {
            return;
        }

        Estado = EstadoDevolucion.RecibidaEnDeposito;
        RecibidaEnDepositoEn = ahora.ToUniversalTime();
    }

    // CU-31 (T15): el comercio recibió el envío devuelto. Quién lo recibió queda registrado para dirimir reclamos
    // (requerimiento especial 1). Sólo se entrega lo que ya está en el depósito.
    public void Cerrar(string nombreReceptor, string documentoReceptor, DateTimeOffset ahora)
    {
        if (Estado == EstadoDevolucion.Cerrada)
        {
            throw new DomainException("La devolución ya se entregó al comercio.");
        }

        if (Estado != EstadoDevolucion.RecibidaEnDeposito)
        {
            throw new DomainException("El envío todavía no volvió al depósito: se entrega al comercio después de la rendición.");
        }

        NombreReceptor = Requerido(nombreReceptor, LargoNombre, "el nombre de quien recibe por el comercio");
        DocumentoReceptor = Requerido(documentoReceptor, LargoDocumento, "el documento de quien recibe por el comercio");
        EntregadaAlComercioEn = ahora.ToUniversalTime();
        Estado = EstadoDevolucion.Cerrada;
    }

    public const int LargoNombre = 200;
    public const int LargoDocumento = 50;

    private static string Requerido(string valor, int largo, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new DomainException($"Falta {campo}.");
        }

        var limpio = valor.Trim();
        return limpio.Length <= largo ? limpio : throw new DomainException($"Revisá {campo}: es demasiado largo.");
    }
}

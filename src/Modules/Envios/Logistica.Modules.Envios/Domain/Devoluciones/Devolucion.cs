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
            Motivo = motivo.Trim(), Estado = EstadoDevolucion.Pendiente, IniciadaEn = ahora,
        };
    }
}

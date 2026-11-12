using System.Security.Cryptography;
using System.Text;
using Logistica.SharedKernel;

namespace Logistica.Modules.Planificacion.Domain.Rutas;

internal sealed class OperacionBultos : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid OperacionId { get; private set; }
    public string Huella { get; private set; } = "";
    public string Resultado { get; private set; } = "";
    private OperacionBultos() { }
    public static string CalcularHuella(string pedido) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pedido)));
    public static OperacionBultos Registrar(Guid operador, Guid operacion, string pedido, string resultado)
    {
        if (operacion == Guid.Empty) throw new DomainException("La operación requiere un identificador.");
        return new() { OperadorId = operador, OperacionId = operacion, Huella = CalcularHuella(pedido), Resultado = resultado };
    }
}

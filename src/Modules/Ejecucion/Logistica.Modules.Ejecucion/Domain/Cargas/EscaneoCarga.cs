using Logistica.SharedKernel;

namespace Logistica.Modules.Ejecucion.Domain.Cargas;

internal sealed class EscaneoCarga : Entity, IOperadorOwned
{
    public Guid OperadorId { get; private set; }
    public Guid OperacionId { get; private set; }
    public Guid RutaId { get; private set; }
    public Guid? BultoId { get; private set; }
    public string CodigoBulto { get; private set; } = "";
    public string Resultado { get; private set; } = "";
    public DateTimeOffset EscaneadoEn { get; private set; }
    private EscaneoCarga() { }
    public static EscaneoCarga Crear(Guid operador, Guid operacion, Guid ruta, Guid? bulto, string codigo, bool esperado, DateTimeOffset fecha)
        => new() { OperadorId = operador, OperacionId = operacion, RutaId = ruta, BultoId = bulto,
            CodigoBulto = codigo, Resultado = esperado ? "Esperado" : "Sobrante", EscaneadoEn = fecha.ToUniversalTime() };
}

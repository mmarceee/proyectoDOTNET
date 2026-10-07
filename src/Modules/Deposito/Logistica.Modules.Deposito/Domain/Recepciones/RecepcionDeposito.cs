using Logistica.SharedKernel;

namespace Logistica.Modules.Deposito.Domain.Recepciones;

// La llegada de un bulto al depósito, verificada contra lo que declaró el comercio (RF 10, CU-30).
// Guarda el EnvioId, además del BultoId, para saber qué bultos de un envío ya llegaron
// sin consultar las tablas de Envíos.
internal sealed class RecepcionDeposito : Entity, IOperadorOwned
{
    // Diferencia admitida entre lo medido y lo declarado antes de considerarla una discrepancia.
    public const decimal Tolerancia = 0.05m;

    public Guid OperadorId { get; private set; }
    public Guid EnvioId { get; private set; }
    public Guid BultoId { get; private set; }
    public DateTimeOffset RecibidoEn { get; private set; }
    public ResultadoRecepcion Resultado { get; private set; }
    public string? Discrepancia { get; private set; }

    private RecepcionDeposito() { } // para EF Core

    // Las discrepancias se registran, no bloquean la recepción (tabla de transiciones, T2; CU-30, A5).
    public static RecepcionDeposito Registrar(Guid operadorId, Guid envioId, Guid bultoId,
        Medidas declaradas, Medidas medidas, DateTimeOffset ahora)
    {
        var diferencias = new List<string>();
        Comparar("peso", "kg", declaradas.PesoKg, medidas.PesoKg, diferencias);
        Comparar("largo", "cm", declaradas.LargoCm, medidas.LargoCm, diferencias);
        Comparar("ancho", "cm", declaradas.AnchoCm, medidas.AnchoCm, diferencias);
        Comparar("alto", "cm", declaradas.AltoCm, medidas.AltoCm, diferencias);

        return new RecepcionDeposito
        {
            OperadorId = operadorId,
            EnvioId = envioId,
            BultoId = bultoId,
            RecibidoEn = ahora,
            Resultado = diferencias.Count == 0 ? ResultadoRecepcion.Conforme : ResultadoRecepcion.ConDiscrepancia,
            Discrepancia = diferencias.Count == 0 ? null : string.Join("; ", diferencias),
        };
    }

    private static void Comparar(string medida, string unidad, decimal? declarado, decimal? medido, List<string> diferencias)
    {
        if (declarado is not decimal valorDeclarado || medido is not decimal valorMedido)
        {
            return;
        }

        if (valorMedido <= 0)
        {
            throw new DomainException($"El {medida} medido debe ser mayor que cero.");
        }

        if (Math.Abs(valorMedido - valorDeclarado) > valorDeclarado * Tolerancia)
        {
            diferencias.Add($"{medida}: declarado {valorDeclarado:0.##} {unidad}, medido {valorMedido:0.##} {unidad}");
        }
    }
}

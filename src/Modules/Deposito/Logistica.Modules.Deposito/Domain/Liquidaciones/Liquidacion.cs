using Logistica.SharedKernel;

namespace Logistica.Modules.Deposito.Domain.Liquidaciones;

// Lo que un comercio debe al operador por los envíos de un período (RF 30, CU-32). Raíz del agregado: crea sus líneas
// y controla el paso de Borrador a Emitida y de Emitida a Pagada.
internal sealed class Liquidacion : Entity, IOperadorOwned, IComercioOwned
{
    private readonly List<LineaLiquidacion> _lineas = [];

    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public DateOnly PeriodoDesde { get; private set; }
    public DateOnly PeriodoHasta { get; private set; }
    public EstadoLiquidacion Estado { get; private set; }
    public decimal TotalBruto { get; private set; }
    // Sin ajustes por ahora: el envío se cobra por la tarifa de su admisión, sin recargos (CU-32, requerimiento 2).
    public decimal TotalAjustes { get; private set; }
    public decimal TotalNeto { get; private set; }
    public DateTimeOffset GeneradaEn { get; private set; }
    public DateTimeOffset? EmitidaEn { get; private set; }
    public DateTimeOffset? PagadaEn { get; private set; }

    public IReadOnlyList<LineaLiquidacion> Lineas => _lineas;

    private Liquidacion() { } // para EF Core

    public static Liquidacion Generar(Guid operadorId, Guid comercioId, DateOnly desde, DateOnly hasta,
        IReadOnlyList<DatosLinea> lineas, DateTimeOffset ahora)
    {
        if (hasta < desde)
            throw new DomainException("El período termina antes de empezar.");
        // A1: sin envíos no hay liquidación.
        if (lineas.Count == 0)
            throw new DomainException("El comercio no tiene envíos sin liquidar en ese período.");
        // A2: un envío aparece una sola vez.
        if (lineas.Select(l => l.EnvioId).Distinct().Count() != lineas.Count)
            throw new DomainException("Un envío no puede aparecer dos veces en la liquidación.");

        var liquidacion = new Liquidacion
        {
            OperadorId = operadorId,
            ComercioId = comercioId,
            PeriodoDesde = desde,
            PeriodoHasta = hasta,
            Estado = EstadoLiquidacion.Borrador,
            GeneradaEn = ahora.ToUniversalTime(),
        };
        foreach (var datos in lineas)
            liquidacion._lineas.Add(new LineaLiquidacion(liquidacion, datos));

        liquidacion.TotalBruto = liquidacion._lineas.Sum(l => l.Monto);
        liquidacion.TotalAjustes = 0;
        liquidacion.TotalNeto = liquidacion.TotalBruto + liquidacion.TotalAjustes;
        return liquidacion;
    }

    // Paso 6: el administrador la revisa y la emite.
    public void Emitir(DateTimeOffset ahora)
    {
        if (Estado != EstadoLiquidacion.Borrador)
            throw new DomainException("Sólo se puede emitir una liquidación en borrador.");
        Estado = EstadoLiquidacion.Emitida;
        EmitidaEn = ahora.ToUniversalTime();
    }

    // Un borrador mal generado se elimina entero y sus envíos quedan libres para otra liquidación. Una emitida ya la
    // recibió el comercio: no se borra.
    public void ValidarEliminacion()
    {
        if (Estado != EstadoLiquidacion.Borrador)
            throw new DomainException("Sólo se puede eliminar una liquidación en borrador.");
    }

    // A3: el pago se registra sobre una liquidación emitida.
    public void MarcarPagada(DateTimeOffset ahora)
    {
        if (Estado != EstadoLiquidacion.Emitida)
            throw new DomainException("Sólo se puede marcar como pagada una liquidación emitida.");
        Estado = EstadoLiquidacion.Pagada;
        PagadaEn = ahora.ToUniversalTime();
    }
}

// Una línea por envío: su número y la fecha en que llegó al estado liquidable, para que el comercio la reconozca (CU-33).
internal sealed class LineaLiquidacion : Entity, IOperadorOwned, IComercioOwned
{
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public Guid LiquidacionId { get; private set; }
    public Guid EnvioId { get; private set; }
    public string NumeroEnvio { get; private set; } = "";
    public DateTimeOffset Fecha { get; private set; }
    public string Concepto { get; private set; } = "";
    public decimal Monto { get; private set; }

    private LineaLiquidacion() { } // para EF Core

    internal LineaLiquidacion(Liquidacion liquidacion, DatosLinea datos)
    {
        OperadorId = liquidacion.OperadorId;
        ComercioId = liquidacion.ComercioId;
        LiquidacionId = liquidacion.Id;
        EnvioId = datos.EnvioId;
        NumeroEnvio = datos.NumeroEnvio;
        Fecha = datos.Fecha.ToUniversalTime();
        Concepto = datos.Concepto;
        Monto = datos.Monto;
    }
}

internal sealed record DatosLinea(Guid EnvioId, string NumeroEnvio, DateTimeOffset Fecha, string Concepto, decimal Monto);

internal enum EstadoLiquidacion
{
    Borrador,
    Emitida,
    Pagada,
}

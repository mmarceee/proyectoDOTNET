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
    // Cierre técnico D8: TotalBruto suma las tarifas base de los envíos; TotalAjustes, los recargos por nueva visita
    // (RF 33); TotalNeto, los dos, sin contar un cargo dos veces.
    public decimal TotalBruto { get; private set; }
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
        // A1: sin conceptos no hay liquidación. Puede tener sólo recargos (D8).
        if (lineas.Count == 0)
            throw new DomainException("El comercio no tiene envíos ni recargos sin liquidar en ese período.");
        // A2: un concepto aparece una sola vez. Un envío puede aportar su base y varios recargos distintos.
        if (lineas.Select(l => (l.Origen, l.IdDeOrigen)).Distinct().Count() != lineas.Count)
            throw new DomainException("Un concepto no puede aparecer dos veces en la liquidación.");

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

        // Las líneas ya están en pesos enteros: los totales las suman sin volver a redondear.
        liquidacion.TotalBruto = liquidacion._lineas.Where(l => l.Origen == OrigenLiquidacion.BaseEnvio).Sum(l => l.Monto);
        liquidacion.TotalAjustes = liquidacion._lineas.Where(l => l.Origen == OrigenLiquidacion.RecargoVisita).Sum(l => l.Monto);
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

// Una línea por concepto de origen (cierre técnico, D8): la tarifa base de un envío terminado o un recargo por nueva
// visita. Lleva el número del envío y la fecha del hecho (cierre del envío o visita) para que el comercio la reconozca
// (CU-33). El índice único sobre (OperadorId, Origen, IdDeOrigen) impide cobrar dos veces el mismo concepto.
internal sealed class LineaLiquidacion : Entity, IOperadorOwned, IComercioOwned
{
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public Guid LiquidacionId { get; private set; }
    public Guid EnvioId { get; private set; }
    public OrigenLiquidacion Origen { get; private set; }
    // El EnvioId para la base; el Id del recargo para un recargo.
    public Guid IdDeOrigen { get; private set; }
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
        Origen = datos.Origen;
        IdDeOrigen = datos.IdDeOrigen;
        NumeroEnvio = datos.NumeroEnvio;
        Fecha = datos.Fecha.ToUniversalTime();
        Concepto = datos.Concepto;
        Monto = EnPesos(datos.Monto);
    }

    // D8: importes en pesos uruguayos enteros, sin centésimos. Cada concepto se normaliza una sola vez, al entrar a la
    // liquidación: al peso más cercano y 0,50 hacia arriba (1500,49 → 1500; 1500,50 → 1501).
    public static decimal EnPesos(decimal monto)
    {
        if (monto < 0)
            throw new DomainException("El importe de un concepto no puede ser negativo.");
        return Math.Round(monto, 0, MidpointRounding.AwayFromZero);
    }
}

// Origen sin IdDeOrigen es la base del envío: su IdDeOrigen es el EnvioId.
internal sealed record DatosLinea(Guid EnvioId, string NumeroEnvio, DateTimeOffset Fecha, string Concepto, decimal Monto,
    OrigenLiquidacion Origen = OrigenLiquidacion.BaseEnvio, Guid? Recargo = null)
{
    public Guid IdDeOrigen => Origen == OrigenLiquidacion.BaseEnvio ? EnvioId
        : Recargo ?? throw new DomainException("Un recargo necesita su identificador.");
}

// Qué se cobra en una línea (D8): la tarifa base del envío terminado o un recargo por nueva visita.
internal enum OrigenLiquidacion
{
    BaseEnvio,
    RecargoVisita,
}

internal enum EstadoLiquidacion
{
    Borrador,
    Emitida,
    Pagada,
}

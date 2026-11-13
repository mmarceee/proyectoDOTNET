using Logistica.Modules.Deposito.Domain.Liquidaciones;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Deposito;

// CU-32: la liquidación suma sus líneas, no se genera vacía, pasa de Borrador a Emitida y de Emitida a Pagada, y sólo
// en borrador se puede eliminar.
public class LiquidacionTests
{
    private static readonly DateOnly Desde = new(2026, 10, 1);
    private static readonly DateOnly Hasta = new(2026, 10, 31);
    private static readonly DateTimeOffset Ahora = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static DatosLinea Linea(decimal monto, Guid? envioId = null) =>
        new(envioId ?? Guid.NewGuid(), "ENV-000001", Ahora.AddDays(-10), "Envío entregado", monto);

    private static Liquidacion Generar(params DatosLinea[] lineas) =>
        Liquidacion.Generar(Guid.NewGuid(), Guid.NewGuid(), Desde, Hasta, lineas, Ahora);

    [Fact]
    public void Se_genera_en_borrador_con_una_linea_por_envio_y_sus_totales()
    {
        var liquidacion = Generar(Linea(150), Linea(300));

        Assert.Equal(EstadoLiquidacion.Borrador, liquidacion.Estado);
        Assert.Equal(2, liquidacion.Lineas.Count);
        Assert.All(liquidacion.Lineas, l => Assert.Equal(liquidacion.Id, l.LiquidacionId));
        Assert.Equal(450m, liquidacion.TotalBruto);
        Assert.Equal(0m, liquidacion.TotalAjustes);
        Assert.Equal(450m, liquidacion.TotalNeto);
    }

    // Cierre técnico D8: la base va en TotalBruto y los recargos en TotalAjustes; un envío aporta su base y varios
    // recargos distintos, y puede haber una liquidación sólo de recargos.
    [Fact]
    public void Los_recargos_van_en_ajustes_y_un_envio_aporta_su_base_y_sus_recargos()
    {
        var envio = Guid.NewGuid();
        var liquidacion = Generar(Linea(450, envio), Recargo(100, envio), Recargo(100, envio));

        Assert.Equal(450m, liquidacion.TotalBruto);
        Assert.Equal(200m, liquidacion.TotalAjustes);
        Assert.Equal(650m, liquidacion.TotalNeto);
        Assert.Equal(0m, Generar(Recargo(80, envio)).TotalBruto);
    }

    [Fact]
    public void Un_recargo_no_aparece_dos_veces()
    {
        var recargo = Guid.NewGuid();
        Assert.Throws<DomainException>(() => Generar(Recargo(100, recargoId: recargo), Recargo(100, recargoId: recargo)));
    }

    // D8: pesos enteros, al peso más cercano y 0,50 hacia arriba, una vez por concepto; los totales no se redondean de nuevo.
    [Theory]
    [InlineData(1500, 1500)]
    [InlineData(1500.49, 1500)]
    [InlineData(1500.50, 1501)]
    public void Cada_concepto_queda_en_pesos_enteros(decimal monto, decimal esperado)
    {
        var liquidacion = Generar(Linea(monto), Recargo(monto));

        Assert.All(liquidacion.Lineas, l => Assert.Equal(esperado, l.Monto));
        Assert.Equal(esperado * 2, liquidacion.TotalNeto);
    }

    private static DatosLinea Recargo(decimal monto, Guid? envioId = null, Guid? recargoId = null) =>
        new(envioId ?? Guid.NewGuid(), "ENV-000001", Ahora.AddDays(-5), "Recargo por nueva visita", monto,
            OrigenLiquidacion.RecargoVisita, recargoId ?? Guid.NewGuid());

    [Fact]
    public void A1_sin_envios_no_se_genera()
    {
        Assert.Throws<DomainException>(() => Generar());
    }

    [Fact]
    public void A2_un_envio_no_aparece_dos_veces()
    {
        var envio = Guid.NewGuid();

        Assert.Throws<DomainException>(() => Generar(Linea(150, envio), Linea(150, envio)));
    }

    [Fact]
    public void Un_periodo_que_termina_antes_de_empezar_se_rechaza()
    {
        Assert.Throws<DomainException>(() =>
            Liquidacion.Generar(Guid.NewGuid(), Guid.NewGuid(), Hasta, Desde, [Linea(150)], Ahora));
    }

    [Fact]
    public void Se_emite_y_despues_se_marca_pagada()
    {
        var liquidacion = Generar(Linea(150));

        liquidacion.Emitir(Ahora);
        Assert.Equal(EstadoLiquidacion.Emitida, liquidacion.Estado);
        Assert.Equal(Ahora, liquidacion.EmitidaEn);

        liquidacion.MarcarPagada(Ahora.AddDays(3));
        Assert.Equal(EstadoLiquidacion.Pagada, liquidacion.Estado);
        Assert.Equal(Ahora.AddDays(3), liquidacion.PagadaEn);
    }

    [Fact]
    public void Solo_un_borrador_se_puede_eliminar()
    {
        var liquidacion = Generar(Linea(150));

        liquidacion.ValidarEliminacion();
        liquidacion.Emitir(Ahora);
        Assert.Throws<DomainException>(liquidacion.ValidarEliminacion);
        liquidacion.MarcarPagada(Ahora);
        Assert.Throws<DomainException>(liquidacion.ValidarEliminacion);
    }

    [Fact]
    public void A3_no_se_paga_un_borrador_ni_se_emite_dos_veces()
    {
        var liquidacion = Generar(Linea(150));

        Assert.Throws<DomainException>(() => liquidacion.MarcarPagada(Ahora));
        liquidacion.Emitir(Ahora);
        Assert.Throws<DomainException>(() => liquidacion.Emitir(Ahora));
        liquidacion.MarcarPagada(Ahora);
        Assert.Throws<DomainException>(() => liquidacion.MarcarPagada(Ahora));
    }
}

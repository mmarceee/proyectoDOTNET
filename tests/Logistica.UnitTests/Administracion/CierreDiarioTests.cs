using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Administracion.Domain.CierresDiarios;
using Logistica.Modules.Planificacion.Contracts.Events;
using Logistica.Modules.Seguimiento.Infrastructure.Messaging;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Administracion;

// CU-70: el período de liquidación, el registro del cierre y la alerta de rutas sin finalizar en el tablero.
public class CierreDiarioTests
{
    [Theory]
    [InlineData(2026, 11, 30, true)]
    [InlineData(2026, 11, 29, false)]
    [InlineData(2028, 2, 29, true)]
    [InlineData(2027, 2, 28, true)]
    [InlineData(2026, 12, 31, true)]
    public void La_liquidacion_se_genera_el_ultimo_dia_del_mes(int anio, int mes, int dia, bool termina)
    {
        var fecha = new DateOnly(anio, mes, dia);

        Assert.Equal(termina, CierreDiario.TerminaPeriodoDeLiquidacion(fecha));
        Assert.Equal(new DateOnly(anio, mes, 1), CierreDiario.InicioPeriodoDeLiquidacion(fecha));
    }

    [Fact]
    public void El_cierre_registra_el_resultado_en_utc_y_rechaza_cantidades_negativas()
    {
        var operador = Guid.NewGuid();
        var ejecutado = new DateTimeOffset(2026, 11, 6, 23, 35, 0, TimeSpan.FromHours(-3));

        var cierre = CierreDiario.Registrar(operador, new DateOnly(2026, 11, 6), 2, 1, 0, ejecutado);

        Assert.Equal((operador, 2, 1, 0), (cierre.OperadorId, cierre.EnviosDevueltos, cierre.RutasSinFinalizar,
            cierre.LiquidacionesGeneradas));
        Assert.Equal(TimeSpan.Zero, cierre.EjecutadoEn.Offset);
        Assert.Throws<DomainException>(() => CierreDiario.Registrar(operador, new DateOnly(2026, 11, 6), -1, 0, 0, ejecutado));
        Assert.Throws<DomainException>(() => CierreDiario.Registrar(Guid.Empty, new DateOnly(2026, 11, 6), 0, 0, 0, ejecutado));
    }

    [Fact]
    public void Las_rutas_sin_finalizar_llegan_al_tablero_como_alerta()
    {
        var mensaje = new MensajeIntegracion(Guid.NewGuid(), RutasSinFinalizar.Tipo, Guid.NewGuid(), null, DateTimeOffset.UtcNow,
            null, JsonSerializer.Serialize(new RutasSinFinalizar(new DateOnly(2026, 11, 6), [Guid.NewGuid(), Guid.NewGuid()])));

        var novedad = NotificarNovedadTablero.Leer(mensaje);

        Assert.True(novedad.Alerta);
        Assert.Contains("2 rutas quedaron sin finalizar", novedad.Texto);
        Assert.Contains(RutasSinFinalizar.Tipo, NotificarNovedadTablero.Tipos);
    }
}

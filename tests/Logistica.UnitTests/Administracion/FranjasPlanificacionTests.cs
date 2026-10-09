using Logistica.Modules.Administracion.Domain.Planificacion;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Administracion;

public class FranjasPlanificacionTests
{
    [Fact]
    public void Cambio_futuro_conserva_la_oferta_hasta_el_dia_anterior_y_encadena_versiones()
    {
        var inicio = new DateOnly(2035, 1, 1);
        var corte = inicio.AddDays(10);
        var anterior = FranjaHoraria.Crear(Guid.NewGuid(), Guid.NewGuid(), new(14, 0), new(18, 0), Enum.GetValues<DayOfWeek>(), inicio);
        var nueva = anterior.Reemplazar(new(9, 0), new(13, 0), Enum.GetValues<DayOfWeek>(), corte);
        Assert.True(anterior.Aplica(corte.AddDays(-1)));
        Assert.False(anterior.Aplica(corte));
        Assert.True(nueva.Aplica(corte));
        Assert.False(nueva.Aplica(corte.AddDays(-1)));
        Assert.Equal(anterior.Id, nueva.ReemplazaAId);
        Assert.Equal(anterior.OperadorId, nueva.OperadorId);
        Assert.Throws<DomainException>(() => anterior.Reemplazar(new(10, 0), new(14, 0), Enum.GetValues<DayOfWeek>(), corte.AddDays(1)));
        var tercera = nueva.Reemplazar(new(10, 0), new(14, 0), Enum.GetValues<DayOfWeek>(), corte.AddDays(10));
        Assert.Equal(nueva.Id, tercera.ReemplazaAId);
    }
}

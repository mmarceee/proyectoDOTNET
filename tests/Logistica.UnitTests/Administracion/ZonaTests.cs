using Logistica.Modules.Administracion.Domain.Planificacion;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Administracion;

public class ZonaTests
{
    private static readonly Guid Operador = Guid.NewGuid();
    private static readonly DateOnly Inicio = new(2035, 1, 1);
    private static readonly DayOfWeek[] LunesAViernes =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private static Zona CrearZona() => Zona.Crear(Operador, "MVD", "Montevideo", ["11000", "11100"]);

    private static FranjaHoraria CrearFranja(Guid zonaId, int desde, int hasta, DayOfWeek[] dias) =>
        FranjaHoraria.Crear(Operador, zonaId, new(desde, 0), new(hasta, 0), dias, Inicio);

    [Fact]
    public void Modificar_cambia_nombre_y_cobertura_pero_no_el_codigo()
    {
        var zona = CrearZona();

        zona.Modificar("Montevideo Centro", ["11000", " 11200 "]);

        Assert.Equal("MVD", zona.Codigo);
        Assert.Equal("Montevideo Centro", zona.Nombre);
        Assert.Equal(["11000", "11200"], zona.CodigosPostales);
    }

    [Fact]
    public void Una_zona_sin_codigos_postales_no_se_puede_modificar()
    {
        var zona = CrearZona();

        Assert.Throws<DomainException>(() => zona.Modificar("Montevideo", []));
    }

    [Fact]
    public void Desactivar_deja_la_zona_inactiva()
    {
        var zona = CrearZona();

        zona.Desactivar();

        Assert.False(zona.Activa);
    }

    [Fact]
    public void Cubre_reconoce_sus_codigos_postales()
    {
        var zona = CrearZona();

        Assert.True(zona.Cubre(" 11100"));
        Assert.False(zona.Cubre("15000"));
    }

    [Fact]
    public void Franjas_que_se_cruzan_el_mismo_dia_se_superponen()
    {
        var zonaId = Guid.NewGuid();
        var manana = CrearFranja(zonaId, 9, 13, LunesAViernes);
        var mediodia = CrearFranja(zonaId, 12, 16, [DayOfWeek.Monday]);

        Assert.True(manana.SeSuperponeCon(mediodia));
        Assert.True(mediodia.SeSuperponeCon(manana));
    }

    [Fact]
    public void Franjas_contiguas_no_se_superponen()
    {
        var zonaId = Guid.NewGuid();
        var manana = CrearFranja(zonaId, 9, 13, LunesAViernes);
        var tarde = CrearFranja(zonaId, 13, 17, LunesAViernes);

        Assert.False(manana.SeSuperponeCon(tarde));
    }

    [Fact]
    public void Franjas_de_dias_distintos_no_se_superponen()
    {
        var zonaId = Guid.NewGuid();
        var semana = CrearFranja(zonaId, 9, 13, LunesAViernes);
        var sabado = CrearFranja(zonaId, 9, 13, [DayOfWeek.Saturday]);

        Assert.False(semana.SeSuperponeCon(sabado));
    }

    [Fact]
    public void Una_franja_reemplazada_no_choca_con_su_reemplazo()
    {
        var anterior = CrearFranja(Guid.NewGuid(), 9, 13, LunesAViernes);

        var nueva = anterior.Reemplazar(new(10, 0), new(14, 0), LunesAViernes, Inicio.AddDays(10));

        Assert.False(anterior.SeSuperponeCon(nueva));
    }
}

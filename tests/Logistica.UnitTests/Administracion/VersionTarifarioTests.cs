using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain;
using Logistica.Modules.Administracion.Domain.Tarifas;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Administracion;

public class VersionTarifarioTests
{
    private static readonly Guid Operador = Guid.NewGuid();
    private static readonly Guid Centro = Guid.NewGuid();
    private static readonly Guid Costa = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2035, 1, 10, 12, 0, 0, TimeSpan.Zero);

    // Centro, Estándar: 0 a 5 kg y 0 a 0,05 m³, $150.
    private static DatosReglaTarifa Regla(decimal pesoDesde = 0, decimal pesoHasta = 5, decimal volDesde = 0,
        decimal volHasta = 0.05m, decimal precio = 150, Guid? zona = null, Modalidad modalidad = Modalidad.Estandar) =>
        new(zona ?? Centro, modalidad, pesoDesde, pesoHasta, volDesde, volHasta, precio);

    private static VersionTarifario Borrador(params DatosReglaTarifa[] reglas)
    {
        var version = VersionTarifario.CrearBorrador(Operador, 1, null);
        foreach (var regla in reglas)
        {
            version.AgregarRegla(regla);
        }

        return version;
    }

    // ReglaTarifa

    [Fact]
    public void Una_regla_con_rango_invertido_o_precio_negativo_no_se_crea()
    {
        var version = Borrador();

        Assert.Throws<DomainException>(() => version.AgregarRegla(Regla(pesoDesde: 5, pesoHasta: 5)));
        Assert.Throws<DomainException>(() => version.AgregarRegla(Regla(precio: -1)));
    }

    // D1: el "desde" está incluido y el "hasta" excluido.
    [Fact]
    public void Un_bulto_en_el_borde_cae_en_el_rango_siguiente()
    {
        var version = Borrador(Regla(0, 5, precio: 150), Regla(5, 10, precio: 250));

        Assert.Equal(150, version.Calcular(Centro, Modalidad.Estandar, 4.99m, 0.01m));
        Assert.Equal(250, version.Calcular(Centro, Modalidad.Estandar, 5, 0.01m));
    }

    // A1: se cruzan en peso y en volumen a la vez.
    [Fact]
    public void Reglas_que_se_cruzan_en_peso_y_volumen_se_superponen()
    {
        var version = Borrador(Regla(0, 5), Regla(3, 8));

        Assert.Single(version.Conflictos());
    }

    [Fact]
    public void Reglas_contiguas_o_que_se_cruzan_en_un_solo_rango_no_se_superponen()
    {
        var version = Borrador(
            Regla(0, 5), Regla(5, 10),                    // contiguas en peso
            Regla(3, 8, volDesde: 0.05m, volHasta: 0.1m), // se cruza en peso pero no en volumen
            Regla(0, 5, modalidad: Modalidad.Urgente),    // otra modalidad
            Regla(0, 5, zona: Costa));                    // otra zona

        Assert.Empty(version.Conflictos());
    }

    // AjusteTarifa y Calcular (D2)

    [Fact]
    public void El_precio_suma_los_ajustes_de_su_modalidad_sobre_el_precio_base()
    {
        var version = Borrador(Regla(modalidad: Modalidad.Urgente, precio: 150));
        version.AgregarAjuste(new DatosAjusteTarifa(TipoAjuste.Recargo, true, 30, Modalidad.Urgente));
        version.AgregarAjuste(new DatosAjusteTarifa(TipoAjuste.Bonificacion, false, 20, Modalidad.Urgente));
        version.AgregarAjuste(new DatosAjusteTarifa(TipoAjuste.Recargo, false, 999, Modalidad.Estandar));

        // 150 + 30 % de 150 − 20 = 175; el recargo de Estándar no le toca.
        Assert.Equal(175, version.Calcular(Centro, Modalidad.Urgente, 2, 0.01m));
    }

    [Fact]
    public void El_precio_nunca_es_negativo_y_sin_regla_no_hay_precio()
    {
        var version = Borrador(Regla(precio: 10));
        version.AgregarAjuste(new DatosAjusteTarifa(TipoAjuste.Bonificacion, false, 50, Modalidad.Estandar));

        Assert.Equal(0, version.Calcular(Centro, Modalidad.Estandar, 2, 0.01m));
        Assert.Null(version.Calcular(Costa, Modalidad.Estandar, 2, 0.01m));
    }

    [Fact]
    public void Un_ajuste_sin_valor_no_se_crea()
    {
        Assert.Throws<DomainException>(() =>
            Borrador().AgregarAjuste(new DatosAjusteTarifa(TipoAjuste.Recargo, true, 0, Modalidad.Estandar)));
    }

    // VersionTarifario

    [Fact]
    public void El_borrador_copia_reglas_y_ajustes_de_la_vigente_sin_tocarla()
    {
        var vigente = Borrador(Regla());
        vigente.AgregarAjuste(new DatosAjusteTarifa(TipoAjuste.Recargo, true, 10, Modalidad.Estandar));
        vigente.Publicar(Ahora, Ahora, null);

        var borrador = VersionTarifario.CrearBorrador(Operador, 2, vigente);

        Assert.Equal(EstadoVersion.Borrador, borrador.Estado);
        Assert.Single(borrador.Reglas);
        Assert.Single(borrador.Ajustes);
        Assert.NotEqual(vigente.Reglas[0].Id, borrador.Reglas[0].Id);
        Assert.Equal(borrador.Id, borrador.Reglas[0].VersionTarifarioId);
    }

    // Letra 6.6: una versión publicada no se modifica.
    [Fact]
    public void Una_version_publicada_no_se_modifica()
    {
        var version = Borrador(Regla());
        version.Publicar(Ahora, Ahora, null);

        Assert.Throws<DomainException>(() => version.AgregarRegla(Regla(5, 10)));
        Assert.Throws<DomainException>(() => version.QuitarRegla(version.Reglas[0].Id));
        Assert.Throws<DomainException>(() => version.Publicar(Ahora.AddDays(1), Ahora, null));
    }

    // A3 y A1.
    [Fact]
    public void No_se_publica_hacia_atras_ni_con_reglas_superpuestas()
    {
        Assert.Throws<DomainException>(() => Borrador(Regla()).Publicar(Ahora.AddMinutes(-1), Ahora, null));
        Assert.Throws<DomainException>(() => Borrador(Regla(0, 5), Regla(3, 8)).Publicar(Ahora, Ahora, null));
    }

    // Pasos 7 a 9 y D3: la anterior rige hasta que empieza la nueva, y nunca hay dos a la vez.
    [Fact]
    public void Publicar_con_fecha_futura_deja_la_anterior_hasta_ese_momento()
    {
        var anterior = Borrador(Regla(precio: 150));
        anterior.Publicar(Ahora, Ahora, null);
        var nueva = VersionTarifario.CrearBorrador(Operador, 2, anterior);
        var desde = Ahora.AddDays(5);

        nueva.Publicar(desde, Ahora, anterior);

        Assert.Equal(EstadoVersion.Reemplazada, anterior.Estado);
        Assert.Equal(desde, anterior.VigenteHasta);
        Assert.True(anterior.RigeEn(desde.AddTicks(-1)));
        Assert.False(anterior.RigeEn(desde));
        Assert.False(nueva.RigeEn(desde.AddTicks(-1)));
        Assert.True(nueva.RigeEn(desde));
    }

    // A2: zonas activas sin precio para alguna modalidad.
    [Fact]
    public void Se_detectan_las_zonas_y_modalidades_sin_tarifa()
    {
        var version = Borrador(Regla(zona: Centro, modalidad: Modalidad.Estandar));

        var faltantes = version.CombinacionesSinTarifa([Centro, Costa]);

        Assert.Equal(3, faltantes.Count);
        Assert.Contains((Centro, Modalidad.Urgente), faltantes);
        Assert.DoesNotContain((Centro, Modalidad.Estandar), faltantes);
    }
}

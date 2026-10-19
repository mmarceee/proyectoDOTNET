using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Domain;
using Logistica.Modules.Administracion.Domain.Reglas;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Administracion;

public class VersionReglasTests
{
    private static readonly Guid Operador = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2035, 1, 10, 12, 0, 0, TimeSpan.Zero);

    // Unas reglas válidas: 3 intentos cada 24 h, sin pruebas extra, devolver en 5 días, Estándar 72 h y Urgente 24 h.
    private static DatosReglas Validas(int maxIntentos = 3, int plazoEntreIntentos = 24, int plazoEstandar = 72,
        bool conUrgente = true) => new(maxIntentos, plazoEntreIntentos, new PruebaExigida(false, false),
        new PoliticaDevolucion(true, 5),
        conUrgente
            ? [new PlazoModalidad(Modalidad.Estandar, plazoEstandar), new PlazoModalidad(Modalidad.Urgente, 24)]
            : [new PlazoModalidad(Modalidad.Estandar, plazoEstandar)],
        24, 20);

    private static readonly DatosMotivo Ausente = new("ausente", "Destinatario ausente", true, false);

    private static VersionReglas Borrador(DatosReglas? valores = null, params DatosMotivo[] motivos) =>
        VersionReglas.CrearBorrador(Operador, 1, valores ?? Validas(), motivos.Length == 0 ? [Ausente] : motivos);

    [Fact]
    public void Unas_reglas_validas_no_tienen_errores_y_se_publican()
    {
        var version = Borrador();

        Assert.Empty(version.Errores());
        version.Publicar(Ahora, Ahora, null);
        Assert.Equal(EstadoVersion.Vigente, version.Estado);
    }

    // A1: cada valor inválido aparece en la lista, todos juntos.
    [Fact]
    public void Los_valores_invalidos_se_informan_todos_juntos()
    {
        var version = Borrador(Validas(maxIntentos: 0, plazoEntreIntentos: 0, conUrgente: false));

        var errores = version.Errores();

        Assert.Equal(3, errores.Count);
        Assert.Contains(errores, e => e.Contains("intentos tiene que ser al menos 1"));
        Assert.Contains(errores, e => e.Contains("plazo entre intentos"));
        Assert.Contains(errores, e => e.Contains("modalidad Urgente"));
        Assert.Throws<DomainException>(() => version.Publicar(Ahora, Ahora, null));
    }

    // A1: códigos repetidos, sin distinguir mayúsculas.
    [Fact]
    public void Un_codigo_de_motivo_repetido_no_deja_publicar()
    {
        var version = Borrador(null, Ausente, new DatosMotivo("AUSENTE", "Otro", false, true));

        Assert.Contains(version.Errores(), e => e.Contains("AUSENTE está repetido"));
    }

    // A2: sin motivos, el repartidor no podría registrar un intento fallido.
    [Fact]
    public void Sin_motivos_de_no_entrega_no_se_publica()
    {
        var version = Borrador();
        version.QuitarMotivo(version.Motivos[0].Id);

        Assert.Contains(version.Errores(), e => e.Contains("catálogo de motivos"));
        Assert.Throws<DomainException>(() => version.Publicar(Ahora, Ahora, null));
    }

    [Fact]
    public void Un_motivo_sin_codigo_no_se_crea()
    {
        Assert.Throws<DomainException>(() => Borrador().AgregarMotivo(new DatosMotivo(" ", "Sin código", true, false)));
    }

    [Fact]
    public void El_borrador_copia_valores_y_motivos_de_la_vigente_con_motivos_nuevos()
    {
        var vigente = Borrador();
        vigente.Publicar(Ahora, Ahora, null);

        var borrador = VersionReglas.CrearBorrador(Operador, 2, vigente.Valores(), vigente.Motivos.Select(m => m.Datos()));

        Assert.Equal(vigente.MaxIntentos, borrador.MaxIntentos);
        Assert.Equal(2, borrador.Plazos.Count);
        Assert.Equal("AUSENTE", borrador.Motivos[0].Codigo);
        Assert.NotEqual(vigente.Motivos[0].Id, borrador.Motivos[0].Id);
        Assert.Equal(borrador.Id, borrador.Motivos[0].VersionReglasId);
    }

    // Letra 6.6: una versión publicada no se modifica.
    [Fact]
    public void Una_version_publicada_no_se_modifica()
    {
        var version = Borrador();
        version.Publicar(Ahora, Ahora, null);

        Assert.Throws<DomainException>(() => version.Modificar(Validas(maxIntentos: 5)));
        Assert.Throws<DomainException>(() => version.AgregarMotivo(new DatosMotivo("NUEVO", "Nuevo", true, false)));
        Assert.Throws<DomainException>(() => version.QuitarMotivo(version.Motivos[0].Id));
    }

    // A3 y la vigencia: igual que el tarifario.
    [Fact]
    public void No_se_publica_hacia_atras_y_la_anterior_rige_hasta_que_empieza_la_nueva()
    {
        Assert.Throws<DomainException>(() => Borrador().Publicar(Ahora.AddMinutes(-1), Ahora, null));

        var anterior = Borrador();
        anterior.Publicar(Ahora, Ahora, null);
        var nueva = VersionReglas.CrearBorrador(Operador, 2, Validas(maxIntentos: 2), [Ausente]);
        var desde = Ahora.AddDays(5);

        nueva.Publicar(desde, Ahora, anterior);

        Assert.Equal(EstadoVersion.Reemplazada, anterior.Estado);
        Assert.True(anterior.RigeEn(desde.AddTicks(-1)));
        Assert.True(nueva.RigeEn(desde));
        Assert.False(nueva.RigeEn(desde.AddTicks(-1)));
    }

    [Fact]
    public void Plazo_por_modalidad_y_motivo_valido_por_codigo()
    {
        var version = Borrador();

        Assert.Equal(TimeSpan.FromHours(72), version.PlazoParaModalidad(Modalidad.Estandar));
        Assert.Equal(TimeSpan.FromHours(24), version.PlazoParaModalidad(Modalidad.Urgente));
        Assert.True(version.MotivoValido(" Ausente "));
        Assert.False(version.MotivoValido("RECHAZADO"));
    }
}

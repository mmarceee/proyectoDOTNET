using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Contracts.Results;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure;

namespace Logistica.UnitTests.Envios;

// Las reglas de entrega que Envíos pide a Administración (CU-06 para CU-17, CU-18 y la hoja de ruta): cada envío se
// juzga con la versión con que se admitió (letra 6.6; escenario 5 de la defensa).
public class ReglasDeEntregaDeAdministracionTests
{
    private static readonly MotivoNoEntregaResultado AusenteV1 =
        new(Guid.NewGuid(), "AUSENTE", "Destinatario ausente", Reprogramable: true, ExigeEvidencia: false);
    private static readonly MotivoNoEntregaResultado AusenteV2 =
        new(Guid.NewGuid(), "AUSENTE", "Nadie en el domicilio", Reprogramable: true, ExigeEvidencia: true);

    // La versión 1 exige sólo la firma; la 2, publicada después, exige foto y no devuelve sola.
    private static readonly ReglasOperativas Version1 = Reglas(1, exigeFoto: false, devolver: true, AusenteV1);
    private static readonly ReglasOperativas Version2 = Reglas(2, exigeFoto: true, devolver: false, AusenteV2);

    [Fact]
    public async Task Un_envio_admitido_con_la_version_1_conserva_sus_reglas_aunque_rija_la_2()
    {
        var reglas = new ReglasDeEntregaDeAdministracion(new AdministracionFalsa(Version2, Version1, Version2));

        var delEnvio = await reglas.DelEnvioAsync(EnvioCon(Version1.VersionId), default);

        Assert.False(delEnvio.ExigeFoto);
        Assert.True(delEnvio.DevolverTrasIntentosAgotados);
    }

    [Fact]
    public async Task Un_envio_admitido_sin_version_usa_la_vigente()
    {
        var reglas = new ReglasDeEntregaDeAdministracion(new AdministracionFalsa(Version2, Version1, Version2));

        var delEnvio = await reglas.DelEnvioAsync(EnvioCon(versionReglasId: null), default);

        Assert.True(delEnvio.ExigeFoto);
        Assert.False(delEnvio.DevolverTrasIntentosAgotados);
    }

    [Fact]
    public async Task Si_la_version_del_envio_no_existe_falla_en_lugar_de_aplicar_otras_reglas()
    {
        var reglas = new ReglasDeEntregaDeAdministracion(new AdministracionFalsa(Version2, Version2));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reglas.DelEnvioAsync(EnvioCon(Version1.VersionId), default));
    }

    [Fact]
    public async Task El_motivo_se_busca_en_el_catalogo_de_la_version_del_envio()
    {
        var reglas = new ReglasDeEntregaDeAdministracion(new AdministracionFalsa(Version2, Version1, Version2));
        var envio = EnvioCon(Version1.VersionId);

        var deSuVersion = await reglas.MotivoAsync(envio, AusenteV1.Id, default);
        var deOtraVersion = await reglas.MotivoAsync(envio, AusenteV2.Id, default);

        Assert.NotNull(deSuVersion);
        Assert.Equal((AusenteV1.Id, "AUSENTE", true, false),
            (deSuVersion.Id, deSuVersion.Codigo, deSuVersion.Reprogramable, deSuVersion.ExigeEvidencia));
        Assert.Null(deOtraVersion);
    }

    [Fact]
    public async Task La_hoja_de_ruta_lleva_la_version_del_envio_y_su_catalogo()
    {
        var reglas = new ReglasDeEntregaDeAdministracion(new AdministracionFalsa(Version2, Version1, Version2));

        var hoja = await reglas.ParaHojaAsync(EnvioCon(Version1.VersionId), default);

        Assert.Equal(("DelEnvio", (Guid?)Version1.VersionId), (hoja.Origen, hoja.VersionId));
        var motivo = Assert.Single(hoja.Motivos);
        Assert.Equal((AusenteV1.Id, "Destinatario ausente"), (motivo.Id, motivo.Nombre));
    }

    [Fact]
    public async Task Dentro_de_un_request_cada_version_se_pide_una_sola_vez()
    {
        var administracion = new AdministracionFalsa(Version2, Version1, Version2);
        var reglas = new ReglasDeEntregaDeAdministracion(administracion);

        await reglas.ParaHojaAsync(EnvioCon(Version1.VersionId), default);
        await reglas.ParaHojaAsync(EnvioCon(Version1.VersionId), default);
        await reglas.ParaHojaAsync(EnvioCon(versionReglasId: null), default);
        await reglas.ParaHojaAsync(EnvioCon(versionReglasId: null), default);

        Assert.Equal(2, administracion.Pedidos);
    }

    private static Envio EnvioCon(Guid? versionReglasId) =>
        Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "ENV-1", new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, null,
            new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero), versionReglasId: versionReglasId);

    private static ReglasOperativas Reglas(int numero, bool exigeFoto, bool devolver, MotivoNoEntregaResultado motivo) =>
        new(Guid.NewGuid(), numero, MaxIntentos: 3, PlazoEntreIntentos: TimeSpan.FromHours(24),
            ExigeFoto: exigeFoto, ExigeNombreYDocumento: false,
            DevolverTrasIntentosAgotados: devolver, PlazoDevolucionDias: 7,
            [new(Modalidad.Estandar, TimeSpan.FromHours(48))], AnticipacionReprogramacion: TimeSpan.FromHours(24),
            MaxParadasPorRuta: 30, [motivo]);

    // Administración con una versión vigente y las versiones publicadas. Cuenta cuántas veces le piden reglas.
    private sealed class AdministracionFalsa(ReglasOperativas? vigente, params ReglasOperativas[] versiones)
        : IAdministracionModuleApi
    {
        public int Pedidos { get; private set; }

        public Task<ReglasOperativas?> ReglasDeEntregaAsync(Guid versionReglasId, CancellationToken ct)
        {
            Pedidos++;
            return Task.FromResult<ReglasOperativas?>(versiones.SingleOrDefault(v => v.VersionId == versionReglasId));
        }

        public Task<ReglasOperativas?> ReglasDeEntregaVigentesAsync(CancellationToken ct)
        {
            Pedidos++;
            return Task.FromResult(vigente);
        }

        // El resto del contrato no lo usa el adaptador.
        public Task<Guid?> ObtenerRelacionComercialActualAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task BloquearPlanificacionAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<ContextoPlanificacion> ObtenerContextoPlanificacionAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<RecursosPlanificacion> ConsultarRecursosPlanificacionAsync(DateOnly fecha, CancellationToken ct) => throw new NotSupportedException();
        public Task<NombresRecursosPlanificacion> ConsultarNombresRecursosAsync(IReadOnlyCollection<Guid> repartidorIds,
            IReadOnlyCollection<Guid> vehiculoIds, CancellationToken ct) => throw new NotSupportedException();
        public Task<ReglasPlanificacion> ObtenerReglasPlanificacionAsync(DateTimeOffset ahora, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<FranjaPlanificacion>> ResolverFranjasAsync(IReadOnlyCollection<Guid> ids, DateOnly fecha, CancellationToken ct) => throw new NotSupportedException();
        public Task<TarifaBulto?> CalcularTarifaAsync(Guid zonaId, Modalidad modalidad, decimal pesoKg, decimal volumenM3, CancellationToken ct) => throw new NotSupportedException();
        public Task<MarcaOperador?> MarcaDelOperadorAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}

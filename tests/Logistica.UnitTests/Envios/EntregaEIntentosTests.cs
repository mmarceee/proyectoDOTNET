using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Envios;

// CU-17 (entrega, T6) y CU-18 (intento fallido, T7 y T9 o T10) en el agregado Envio.
public class EntregaEIntentosTests
{
    private static readonly DateTimeOffset Salida = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EnLaCalle = new(2026, 10, 22, 11, 30, 0, TimeSpan.FromHours(-3));
    private static readonly Ubicacion EnLaPuerta = new(-34.9m, -56.16m);

    // Reglas mínimas: sólo la firma, que no se configura.
    private static readonly ReglasEntrega SoloFirma = new(false, false, maxIntentos: 3, TimeSpan.FromHours(24));
    private static readonly ReglasEntrega Exigentes = new(exigeFoto: true, exigeNombreYDocumento: true, 3, TimeSpan.FromHours(24));

    private static readonly MotivoNoEntrega Ausente = new(Guid.NewGuid(), "AUSENTE", reprogramable: true, exigeEvidencia: false);
    private static readonly MotivoNoEntrega Rechazado = new(Guid.NewGuid(), "RECHAZADO", reprogramable: false, exigeEvidencia: false);
    private static readonly MotivoNoEntrega SinAcceso = new(Guid.NewGuid(), "SIN-ACCESO", reprogramable: true, exigeEvidencia: true);

    // Un envío que ya salió en la camioneta: T2, T3 y T5.
    private static Envio EnTransito()
    {
        var envio = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "ENV-1",
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, null, Salida.AddDays(-1));
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, Salida.AddHours(-12));
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, Salida.AddHours(-1));
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, Salida);
        return envio;
    }

    private static PruebaEntrega Prueba(bool firma = true, bool foto = false, string? nombre = null, string? documento = null) =>
        new(firma ? Guid.NewGuid() : null, foto ? Guid.NewGuid() : null, nombre, documento, EnLaPuerta, EnLaCalle);

    // ---- CU-17 · Entrega ----

    [Fact]
    public void Una_entrega_con_firma_deja_el_envio_Entregado_con_su_intento_y_la_hora_del_dispositivo()
    {
        var envio = EnTransito();
        var responsable = Guid.NewGuid();

        var intento = envio.RegistrarEntrega(Prueba(), SoloFirma, responsable, EnLaCalle);

        Assert.Equal(EstadoEnvio.Entregado, envio.Estado);
        Assert.Equal(1, intento.NumeroIntento);
        Assert.Equal(ResultadoIntento.Exitoso, intento.Resultado);
        var evento = envio.Eventos[^1];
        Assert.Equal((EstadoEnvio.EnTransito, EstadoEnvio.Entregado), (evento.EstadoAnterior, evento.EstadoNuevo));
        Assert.Equal(OrigenEvento.AppRepartidor, evento.Origen);
        Assert.Equal(responsable, evento.ResponsableId);
        Assert.Equal(EnLaCalle, evento.OcurridoEn);
        Assert.Equal((EnLaPuerta.Latitud, EnLaPuerta.Longitud), (evento.Latitud, evento.Longitud));
    }

    [Fact]
    public void Sin_la_foto_que_exigen_las_reglas_se_rechaza_y_el_envio_no_cambia()
    {
        var envio = EnTransito();

        Assert.Throws<DomainException>(() =>
            envio.RegistrarEntrega(Prueba(nombre: "Ana", documento: "12345678"), Exigentes, null, EnLaCalle));

        Assert.Equal(EstadoEnvio.EnTransito, envio.Estado);
        Assert.Empty(envio.Intentos);
    }

    [Theory]
    [InlineData(null, "12345678")]
    [InlineData("Ana", null)]
    [InlineData(" ", "12345678")]
    public void Sin_el_nombre_o_el_documento_que_exigen_las_reglas_se_rechaza(string? nombre, string? documento)
    {
        var envio = EnTransito();

        Assert.Throws<DomainException>(() =>
            envio.RegistrarEntrega(Prueba(foto: true, nombre: nombre, documento: documento), Exigentes, null, EnLaCalle));

        Assert.Empty(envio.Intentos);
    }

    [Fact]
    public void Con_las_pruebas_que_exigen_las_reglas_se_entrega()
    {
        var envio = EnTransito();

        envio.RegistrarEntrega(Prueba(foto: true, nombre: "Ana", documento: "12345678"), Exigentes, null, EnLaCalle);

        Assert.Equal(EstadoEnvio.Entregado, envio.Estado);
    }

    [Fact]
    public void Sin_firma_no_hay_entrega_aunque_las_reglas_no_exijan_nada_mas()
    {
        var envio = EnTransito();

        Assert.Throws<DomainException>(() => envio.RegistrarEntrega(Prueba(firma: false), SoloFirma, null, EnLaCalle));

        Assert.Equal(EstadoEnvio.EnTransito, envio.Estado);
        Assert.Empty(envio.Intentos);
    }

    [Fact]
    public void Un_envio_que_no_esta_EnTransito_no_se_puede_entregar_y_no_cambia()
    {
        var envio = EnTransito();
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, EnLaCalle); // T8, reintegrado

        Assert.Throws<TransicionInvalidaException>(() => envio.RegistrarEntrega(Prueba(), SoloFirma, null, EnLaCalle));

        Assert.Equal(EstadoEnvio.EnDeposito, envio.Estado);
        Assert.Empty(envio.Intentos);
    }

    // ---- CU-18 · Intento fallido ----

    [Fact]
    public void Un_intento_fallido_reprogramable_pasa_por_NoEntregado_y_queda_Reprogramado_despues_del_plazo()
    {
        var envio = EnTransito();

        var intento = envio.RegistrarIntentoFallido(Ausente, null, " Nadie en el domicilio ", EnLaPuerta, SoloFirma, null, EnLaCalle);

        Assert.Equal(EstadoEnvio.Reprogramado, envio.Estado);
        Assert.Equal(ResultadoIntento.Fallido, intento.Resultado);
        Assert.Equal(Ausente.Id, intento.MotivoNoEntregaId);
        Assert.Equal("Nadie en el domicilio", intento.Observaciones);
        Assert.Equal(EnLaCalle.AddHours(24), envio.ReintentoDesde);

        // T7 y T9 en la misma operación, en orden y con la misma hora.
        var t7 = envio.Eventos[^2];
        var t9 = envio.Eventos[^1];
        Assert.Equal((EstadoEnvio.EnTransito, EstadoEnvio.NoEntregado, OrigenEvento.AppRepartidor), (t7.EstadoAnterior, t7.EstadoNuevo, t7.Origen));
        Assert.Equal((EstadoEnvio.NoEntregado, EstadoEnvio.Reprogramado, OrigenEvento.Sistema), (t9.EstadoAnterior, t9.EstadoNuevo, t9.Origen));
        Assert.Equal(EnLaCalle, t7.OcurridoEn);
        Assert.Equal(EnLaCalle, t9.OcurridoEn);
        Assert.Equal(EnLaPuerta.Latitud, t7.Latitud);
    }

    [Fact]
    public void Un_motivo_no_reprogramable_inicia_la_devolucion_en_el_primer_intento()
    {
        var envio = EnTransito();

        envio.RegistrarIntentoFallido(Rechazado, null, null, EnLaPuerta, SoloFirma, null, EnLaCalle);

        Assert.Equal(EstadoEnvio.EnDevolucion, envio.Estado);
        Assert.Null(envio.ReintentoDesde);
        Assert.Equal((EstadoEnvio.NoEntregado, EstadoEnvio.EnDevolucion), (envio.Eventos[^1].EstadoAnterior, envio.Eventos[^1].EstadoNuevo));
    }

    [Fact]
    public void Al_agotar_los_intentos_se_inicia_la_devolucion()
    {
        var envio = EnTransito();
        var dosIntentos = new ReglasEntrega(false, false, maxIntentos: 2, TimeSpan.FromHours(24));

        envio.RegistrarIntentoFallido(Ausente, null, null, EnLaPuerta, dosIntentos, null, EnLaCalle);
        Assert.Equal(EstadoEnvio.Reprogramado, envio.Estado);

        // Vuelve a salir en otra ruta (T13 y T5) y falla de nuevo.
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, EnLaCalle.AddDays(1));
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, EnLaCalle.AddDays(1));
        var segundo = envio.RegistrarIntentoFallido(Ausente, null, null, EnLaPuerta, dosIntentos, null, EnLaCalle.AddDays(1));

        Assert.Equal(2, segundo.NumeroIntento);
        Assert.Equal(EstadoEnvio.EnDevolucion, envio.Estado);
    }

    [Fact]
    public void Un_motivo_que_exige_evidencia_sin_foto_se_rechaza_y_el_envio_no_cambia()
    {
        var envio = EnTransito();

        Assert.Throws<DomainException>(() =>
            envio.RegistrarIntentoFallido(SinAcceso, null, null, EnLaPuerta, SoloFirma, null, EnLaCalle));

        Assert.Equal(EstadoEnvio.EnTransito, envio.Estado);
        Assert.Empty(envio.Intentos);
    }

    [Fact]
    public void Un_motivo_que_exige_evidencia_se_acepta_con_la_foto()
    {
        var envio = EnTransito();

        var intento = envio.RegistrarIntentoFallido(SinAcceso, Prueba(firma: false, foto: true), null, EnLaPuerta, SoloFirma, null, EnLaCalle);

        Assert.NotNull(intento.Evidencia?.FotoArchivoId);
        Assert.Equal(EstadoEnvio.Reprogramado, envio.Estado);
    }

    [Fact]
    public void Un_envio_ya_entregado_no_admite_un_intento_fallido()
    {
        var envio = EnTransito();
        envio.RegistrarEntrega(Prueba(), SoloFirma, null, EnLaCalle);

        Assert.Throws<TransicionInvalidaException>(() =>
            envio.RegistrarIntentoFallido(Ausente, null, null, EnLaPuerta, SoloFirma, null, EnLaCalle));

        Assert.Single(envio.Intentos);
        Assert.Equal(EstadoEnvio.Entregado, envio.Estado);
    }

    // ---- Reglas ----

    [Fact]
    public void Las_reglas_exigen_al_menos_un_intento_y_un_plazo_no_negativo()
    {
        Assert.Throws<DomainException>(() => new ReglasEntrega(false, false, 0, TimeSpan.Zero));
        Assert.Throws<DomainException>(() => new ReglasEntrega(false, false, 1, TimeSpan.FromHours(-1)));
    }
}

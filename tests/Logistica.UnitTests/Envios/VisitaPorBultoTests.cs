using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Envios;

// RF 31 y RF 33: una visita del envío con el resultado de cada bulto (decisiones-entrega-parcial.md, secciones 4 y 5).
// El escenario de la demo: tres bultos, dos entregados y uno con motivo.
public class VisitaPorBultoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);
    private static readonly DatosBulto UnBulto = new(PesoKg: 2, LargoCm: 30, AnchoCm: 20, AltoCm: 10, MontoTarifa: 150);
    private static readonly Ubicacion Lugar = new(-34.9m, -56.16m);
    private static readonly PruebaEntrega Firma = new(Guid.NewGuid(), null, null, null, Lugar, Ahora);
    private static readonly MotivoNoEntrega Ausente = new(Guid.NewGuid(), "AUSENTE", reprogramable: true, exigeEvidencia: false);
    private static readonly MotivoNoEntrega Rechazado = new(Guid.NewGuid(), "RECHAZADO", reprogramable: false, exigeEvidencia: false);

    private static ReglasEntrega Reglas(bool parcial = true, int maxIntentos = 3,
        PoliticaPendientes pendientes = PoliticaPendientes.NuevaVisitaSinRecargo) =>
        new(exigeFoto: false, exigeNombreYDocumento: false, maxIntentos, TimeSpan.FromHours(24),
            devolverTrasIntentosAgotados: true, admiteEntregaParcial: parcial, pendientes: pendientes);

    [Fact]
    public void Dos_de_tres_entregados_y_uno_ausente_queda_entregado_parcialmente_y_en_curso()
    {
        var envio = EnvioEnTransito(3);
        var b = envio.Bultos;

        var intentos = envio.RegistrarVisita([new(b[0].Id), new(b[1].Id), new(b[2].Id, Ausente)], Firma, null, null, Lugar,
            Reglas(), null, Ahora);

        Assert.Equal((ResumenEnvio.EntregadoParcialmente, false, 2), (envio.Resumen, envio.Terminado, envio.BultosEntregados));
        Assert.Equal((EstadoEnvio.Reprogramado, 1), (b[2].Estado, b[2].IntentosFallidos));
        Assert.All(b.Take(2), x => Assert.Equal((EstadoEnvio.Entregado, 0), (x.Estado, x.IntentosFallidos)));
        // La firma respalda sólo a los dos entregados; el intento fallido, sólo al tercero.
        Assert.Equal(new[] { b[0].Id, b[1].Id }, intentos.Single(i => i.Resultado == ResultadoIntento.Exitoso).BultoIds);
        Assert.Equal(new[] { b[2].Id }, intentos.Single(i => i.Resultado == ResultadoIntento.Fallido).BultoIds);
        Assert.Equal(Ahora.AddHours(24), envio.ReintentoDesde);
    }

    // Con un motivo no reprogramable el pendiente va a devolución; el envío ya terminó cuando el comercio lo recibe.
    [Fact]
    public void Un_bulto_rechazado_va_a_devolucion_y_el_envio_sigue_parcial()
    {
        var envio = EnvioEnTransito(3);
        var b = envio.Bultos;

        envio.RegistrarVisita([new(b[0].Id), new(b[1].Id), new(b[2].Id, Rechazado)], Firma, null, null, Lugar, Reglas(), null,
            Ahora);

        Assert.Equal(EstadoEnvio.EnDevolucion, b[2].Estado);
        Assert.Equal((EstadoEnvio.EnDevolucion, ResumenEnvio.EntregadoParcialmente, false),
            (envio.Estado, envio.Resumen, envio.Terminado));
    }

    [Fact]
    public void Todos_entregados_queda_Entregado_y_terminado()
    {
        var envio = EnvioEnTransito(2);

        envio.RegistrarVisita(envio.Bultos.Select(x => new ResultadoBulto(x.Id)).ToList(), Firma, null, null, Lugar, Reglas(),
            null, Ahora);

        Assert.Equal((EstadoEnvio.Entregado, ResumenEnvio.Entregado, true, 2),
            (envio.Estado, envio.Resumen, envio.Terminado, envio.BultosEntregados));
    }

    // Una revisita fallida no borra entregas anteriores ni fuerza NoEntregado en el resumen (sección 4 de la decisión).
    [Fact]
    public void Una_visita_sin_entregas_no_cambia_el_resumen_de_un_envio_ya_parcial()
    {
        var envio = EnvioEnTransito(2);
        var b = envio.Bultos;
        envio.RegistrarVisita([new(b[0].Id), new(b[1].Id, Ausente)], Firma, null, null, Lugar, Reglas(), null, Ahora);
        LlevarAEnTransito(envio, b[1]);

        envio.RegistrarVisita([new(b[1].Id, Ausente)], null, null, null, Lugar, Reglas(), null, Ahora.AddDays(1));

        Assert.Equal((ResumenEnvio.EntregadoParcialmente, 2), (envio.Resumen, b[1].IntentosFallidos));
    }

    // Los intentos se cuentan por bulto: el que ya falló una vez agota los suyos antes que un bulto nuevo.
    [Fact]
    public void Al_agotar_los_intentos_del_bulto_va_a_devolucion()
    {
        var envio = EnvioEnTransito(2);
        var b = envio.Bultos;
        envio.RegistrarVisita([new(b[0].Id), new(b[1].Id, Ausente)], Firma, null, null, Lugar, Reglas(maxIntentos: 2), null,
            Ahora);
        LlevarAEnTransito(envio, b[1]);

        envio.RegistrarVisita([new(b[1].Id, Ausente)], null, null, null, Lugar, Reglas(maxIntentos: 2), null, Ahora.AddDays(1));

        Assert.Equal(EstadoEnvio.EnDevolucion, b[1].Estado);
    }

    // RF 33: si las reglas del envío no admiten parcial, se entregan todos o ninguno, y el envío queda igual.
    [Fact]
    public void Sin_entrega_parcial_una_visita_mixta_se_rechaza_entera()
    {
        var envio = EnvioEnTransito(2);
        var b = envio.Bultos;

        Assert.Throws<DomainException>(() => envio.RegistrarVisita([new(b[0].Id), new(b[1].Id, Ausente)], Firma, null, null,
            Lugar, Reglas(parcial: false), null, Ahora));

        Assert.All(b, x => Assert.Equal(EstadoEnvio.EnTransito, x.Estado));
        Assert.Empty(envio.Intentos);
        Assert.Equal(ResumenEnvio.EnOperacion, envio.Resumen);
    }

    [Fact]
    public void La_visita_tiene_que_informar_cada_bulto_en_transito_una_sola_vez()
    {
        var envio = EnvioEnTransito(2);
        var b = envio.Bultos;

        Assert.Throws<DomainException>(() => envio.RegistrarVisita([new(b[0].Id)], Firma, null, null, Lugar, Reglas(), null, Ahora));
        Assert.Throws<DomainException>(() => envio.RegistrarVisita([new(b[0].Id), new(b[0].Id)], Firma, null, null, Lugar,
            Reglas(), null, Ahora));
        Assert.Throws<DomainException>(() => envio.RegistrarVisita([new(b[0].Id), new(Guid.NewGuid())], Firma, null, null,
            Lugar, Reglas(), null, Ahora));
    }

    // RF 33: con la política Devolver, el bulto pendiente va a devolución aunque el motivo sea reprogramable y le queden
    // intentos. Las entregas de la misma visita se mantienen.
    [Fact]
    public void Con_la_politica_devolver_el_pendiente_va_a_devolucion_aunque_le_queden_intentos()
    {
        var envio = EnvioEnTransito(3);
        var b = envio.Bultos;

        envio.RegistrarVisita([new(b[0].Id), new(b[1].Id), new(b[2].Id, Ausente)], Firma, null, null, Lugar,
            Reglas(pendientes: PoliticaPendientes.Devolver), null, Ahora);

        Assert.Equal((EstadoEnvio.EnDevolucion, 1), (b[2].Estado, b[2].IntentosFallidos));
        Assert.Equal((EstadoEnvio.EnDevolucion, ResumenEnvio.EntregadoParcialmente, 2),
            (envio.Estado, envio.Resumen, envio.BultosEntregados));
        Assert.Null(envio.ReintentoDesde);
    }

    // RF 33: con recargo, el pendiente vuelve a visitarse igual que sin recargo (el cobro se registra en otra etapa).
    [Fact]
    public void Con_la_politica_de_recargo_el_pendiente_queda_reprogramado()
    {
        var envio = EnvioEnTransito(2);
        var b = envio.Bultos;

        envio.RegistrarVisita([new(b[0].Id), new(b[1].Id, Ausente)], Firma, null, null, Lugar,
            Reglas(pendientes: PoliticaPendientes.NuevaVisitaConRecargo), null, Ahora);

        Assert.Equal(EstadoEnvio.Reprogramado, b[1].Estado);
    }

    [Fact]
    public void Una_entrega_sin_prueba_se_rechaza()
    {
        var envio = EnvioEnTransito(1);

        Assert.Throws<DomainException>(() => envio.RegistrarVisita([new(envio.Bultos[0].Id)], null, null, null, Lugar,
            Reglas(), null, Ahora));
    }

    private static Envio EnvioEnTransito(int bultos)
    {
        var envio = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "ENV-1", new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            Enumerable.Repeat(UnBulto, bultos).ToList(), OrigenEvento.PortalComercio, responsableId: null, Ahora);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, Ahora);
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, Ahora);
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, Ahora);
        return envio;
    }

    // La revisita (RF 34) todavía no tiene operación propia: el envío entero vuelve a ruta y sale, y el puente mueve al
    // único bulto pendiente.
    private static void LlevarAEnTransito(Envio envio, Bulto pendiente)
    {
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, Ahora);
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, Ahora);
        Assert.Equal(EstadoEnvio.EnTransito, pendiente.Estado);
    }
}

using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Envios;

// RF 31: estado por bulto y resumen derivado del envío. Los casos de CalcularResumen son los diez ejemplos de
// aceptación de decisiones-entrega-parcial.md, sección 3.
public class ResumenEnvioTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly DatosBulto UnBulto = new(PesoKg: 2, LargoCm: 30, AnchoCm: 20, AltoCm: 10, MontoTarifa: 150);

    private const EstadoEnvio Adm = EstadoEnvio.Admitido;
    private const EstadoEnvio Dep = EstadoEnvio.EnDeposito;
    private const EstadoEnvio Asig = EstadoEnvio.AsignadoARuta;
    private const EstadoEnvio Tra = EstadoEnvio.EnTransito;
    private const EstadoEnvio Rep = EstadoEnvio.Reprogramado;
    private const EstadoEnvio Ent = EstadoEnvio.Entregado;
    private const EstadoEnvio Can = EstadoEnvio.Cancelado;

    // Los tipos del dominio son internal: los casos van como texto (bultos separados por coma, anterior, esperado).
    [Theory]
    [InlineData("Admitido,Admitido,Admitido", "Creado", "Creado")]
    [InlineData("EnDeposito,Admitido,Admitido", "Creado", "EnOperacion")]
    [InlineData("EnTransito,EnTransito,AsignadoARuta", "EnOperacion", "EnOperacion")]
    [InlineData("Entregado,Entregado,Reprogramado", "EnOperacion", "EntregadoParcialmente")]
    [InlineData("Entregado,Entregado,Devuelto", "EntregadoParcialmente", "EntregadoParcialmente")]
    [InlineData("Entregado,Extraviado,Extraviado", "EntregadoParcialmente", "EntregadoParcialmente")]
    [InlineData("Entregado,Entregado,Entregado", "EntregadoParcialmente", "Entregado")]
    [InlineData("Devuelto,Devuelto,Extraviado", "EnOperacion", "EnOperacion")]
    [InlineData("Cancelado,Cancelado", "Creado", "Creado")]
    // Inalcanzable con T17 (sólo cancela antes de recibir), pero la regla no baja de En operación a Creado.
    [InlineData("Cancelado,Cancelado", "EnOperacion", "EnOperacion")]
    public void El_resumen_se_deriva_de_los_bultos(string bultos, string anterior, string esperado)
    {
        var estados = bultos.Split(',').Select(Enum.Parse<EstadoEnvio>).ToList();

        Assert.Equal(Enum.Parse<ResumenEnvio>(esperado), Envio.CalcularResumen(estados, Enum.Parse<ResumenEnvio>(anterior)));
    }

    [Fact]
    public void Un_envio_nuevo_tiene_sus_bultos_Admitidos_y_queda_Creado()
    {
        var envio = CrearEnvio(2);

        Assert.All(envio.Bultos, b => Assert.Equal(Adm, b.Estado));
        Assert.Equal((ResumenEnvio.Creado, false, 0), (envio.Resumen, envio.Terminado, envio.BultosEntregados));
    }

    // Puente de compatibilidad (ADR-0007, sección 2.4): los flujos que mueven el envío entero mueven sus bultos.
    [Fact]
    public void Una_entrega_completa_entrega_todos_los_bultos_y_termina_el_envio()
    {
        var envio = CrearEnvio(2);

        envio.Transicionar(Dep, OrigenEvento.Backoffice, null, Ahora);
        Assert.Equal(ResumenEnvio.EnOperacion, envio.Resumen);
        envio.Transicionar(Asig, OrigenEvento.Backoffice, null, Ahora);
        envio.Transicionar(Tra, OrigenEvento.AppRepartidor, null, Ahora);
        envio.Transicionar(Ent, OrigenEvento.AppRepartidor, null, Ahora);

        Assert.All(envio.Bultos, b => Assert.Equal(Ent, b.Estado));
        Assert.Equal((ResumenEnvio.Entregado, true, 2), (envio.Resumen, envio.Terminado, envio.BultosEntregados));
    }

    [Fact]
    public void Un_envio_cancelado_termina_sin_haber_salido_de_Creado()
    {
        var envio = CrearEnvio(2);

        envio.Transicionar(Can, OrigenEvento.PortalComercio, null, Ahora);

        Assert.Equal((ResumenEnvio.Creado, true), (envio.Resumen, envio.Terminado));
    }

    // Cierre técnico D8: el instante en que Terminado pasa de falso a verdadero queda guardado; es el período en que se
    // liquida la base. Mientras quede un bulto sin terminar, no hay instante de cierre.
    [Fact]
    public void El_envio_guarda_cuando_termina_su_ultimo_bulto()
    {
        var envio = CrearEnvio(2);
        envio.Transicionar(Dep, OrigenEvento.Backoffice, null, Ahora);
        envio.Transicionar(Asig, OrigenEvento.Backoffice, null, Ahora);
        Assert.Null(envio.TerminadoEn);

        var cierre = Ahora.AddHours(5);
        envio.Transicionar(Tra, OrigenEvento.AppRepartidor, null, Ahora.AddHours(1));
        envio.Transicionar(Ent, OrigenEvento.AppRepartidor, null, cierre);

        Assert.True(envio.Terminado);
        Assert.Equal(cierre, envio.TerminadoEn);
    }

    [Fact]
    public void Un_intento_fallido_suma_un_intento_a_cada_bulto_y_reprogramar_no()
    {
        var envio = CrearEnvio(2);
        envio.Transicionar(Dep, OrigenEvento.Backoffice, null, Ahora);
        envio.Transicionar(Asig, OrigenEvento.Backoffice, null, Ahora);
        envio.Transicionar(Tra, OrigenEvento.AppRepartidor, null, Ahora);

        envio.Transicionar(EstadoEnvio.NoEntregado, OrigenEvento.AppRepartidor, null, Ahora);
        envio.Transicionar(Rep, OrigenEvento.Sistema, null, Ahora);

        Assert.All(envio.Bultos, b => Assert.Equal((Rep, 1), (b.Estado, b.IntentosFallidos)));
        Assert.Equal((ResumenEnvio.EnOperacion, false), (envio.Resumen, envio.Terminado));
    }

    // T2 por bulto (CU-30): con el primero el envío está En operación; el estado de compatibilidad cambia con el último.
    [Fact]
    public void Recibir_el_primer_bulto_pone_el_envio_En_operacion_y_el_ultimo_lo_deja_EnDeposito()
    {
        var envio = CrearEnvio(2);

        Assert.False(envio.RecibirBulto(envio.Bultos[0].Id, null, Ahora));
        Assert.Equal((Adm, ResumenEnvio.EnOperacion), (envio.Estado, envio.Resumen));
        Assert.Equal((Dep, Adm), (envio.Bultos[0].Estado, envio.Bultos[1].Estado));

        Assert.True(envio.RecibirBulto(envio.Bultos[1].Id, null, Ahora));
        Assert.Equal(Dep, envio.Estado);
        Assert.All(envio.Bultos, b => Assert.Equal(Dep, b.Estado));
    }

    [Fact]
    public void Un_bulto_ya_recibido_o_de_otro_envio_se_rechaza()
    {
        var envio = CrearEnvio(2);
        envio.RecibirBulto(envio.Bultos[0].Id, null, Ahora);

        Assert.Throws<TransicionInvalidaException>(() => envio.RecibirBulto(envio.Bultos[0].Id, null, Ahora));
        Assert.Throws<DomainException>(() => envio.RecibirBulto(Guid.NewGuid(), null, Ahora));
    }

    [Fact]
    public void Recibir_solo_aplica_T2_al_bulto_admitido_aunque_otro_ya_este_en_ruta()
    {
        var envio = CrearEnvio(2);
        var primero = envio.Bultos[0];
        var segundo = envio.Bultos[1];
        envio.RecibirBulto(primero.Id, null, Ahora);
        envio.CambiarBultosDeRuta([primero.Id], Asig, null, Ahora);
        envio.CargarBultos([primero.Id], null, Ahora);

        Assert.Equal(Adm, envio.Estado);
        Assert.Throws<TransicionInvalidaException>(() => envio.RecibirBulto(primero.Id, null, Ahora));
        Assert.False(envio.RecibirBulto(segundo.Id, null, Ahora));
        Assert.Equal((Tra, Dep), (primero.Estado, segundo.Estado));
    }

    // T17: con un bulto ya recibido, el envío no se cancela.
    [Fact]
    public void Un_envio_con_un_bulto_recibido_no_se_puede_cancelar()
    {
        var envio = CrearEnvio(2);
        envio.RecibirBulto(envio.Bultos[0].Id, null, Ahora);

        Assert.Throws<DomainException>(() => envio.Transicionar(Can, OrigenEvento.PortalComercio, null, Ahora));
        Assert.Equal((Adm, ResumenEnvio.EnOperacion), (envio.Estado, envio.Resumen));
    }

    // La misma tabla rige para el bulto: un par que no está se rechaza y el bulto queda igual.
    [Fact]
    public void Un_bulto_no_hace_una_transicion_que_no_esta_en_la_tabla()
    {
        var bulto = CrearEnvio(1).Bultos[0];

        Assert.Throws<TransicionInvalidaException>(() => bulto.Transicionar(Ent));
        Assert.Equal(Adm, bulto.Estado);
    }

    private static Envio CrearEnvio(int bultos) =>
        Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "ENV-1", new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            Enumerable.Repeat(UnBulto, bultos).ToList(), OrigenEvento.PortalComercio, responsableId: null, Ahora);
}

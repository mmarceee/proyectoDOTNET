using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.Modules.Seguimiento.Infrastructure.Messaging;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Seguimiento;

public class EntregaAvisoTests
{
    private static readonly Guid Operador = Guid.NewGuid();
    private static readonly Guid Comercio = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static SuscripcionAviso Suscripcion(params TipoEventoAviso[] tipos) =>
        SuscripcionAviso.Crear(Operador, Comercio, Guid.NewGuid(), "https://comercio.example/avisos",
            tipos.Length == 0 ? [TipoEventoAviso.CambioDeEstado] : tipos, "secreto-cifrado");

    private static EnvioSeguido Envio(Guid? comercio = null) =>
        EnvioSeguido.Crear(Operador, comercio ?? Comercio, Guid.NewGuid(), "ENV-000123");

    private static EntregaAviso Nueva() =>
        EntregaAviso.Crear(Suscripcion(), Guid.NewGuid(), Envio(), "Entregado", TipoEventoAviso.CambioDeEstado, Ahora);

    [Theory]
    [InlineData("EnDevolucion", true, "Devolucion")]
    [InlineData("EnDevolucion", false, "Devolucion")]
    [InlineData("Reprogramado", true, "EntregaFallida")]
    [InlineData("Entregado", false, "CambioDeEstado")]
    [InlineData("Admitido", false, "CambioDeEstado")]
    public void Cada_hecho_se_clasifica_en_un_unico_tipo(string estado, bool intentoFallido, string esperado) =>
        Assert.Equal(esperado, ClasificadorAvisos.Clasificar(estado, intentoFallido).ToString());

    [Fact]
    public void CambioDeEstado_recibe_todos_los_hechos_y_los_otros_tipos_solo_el_suyo()
    {
        var todos = Suscripcion(TipoEventoAviso.CambioDeEstado);
        var fallidas = Suscripcion(TipoEventoAviso.EntregaFallida);
        Assert.All(Enum.GetValues<TipoEventoAviso>(), t => Assert.True(todos.Recibe(t)));
        Assert.True(fallidas.Recibe(TipoEventoAviso.EntregaFallida));
        Assert.False(fallidas.Recibe(TipoEventoAviso.Devolucion));
        Assert.False(fallidas.Recibe(TipoEventoAviso.CambioDeEstado));

        todos.Desactivar();
        Assert.False(todos.Recibe(TipoEventoAviso.CambioDeEstado));
    }

    [Fact]
    public void Crear_copia_los_datos_del_hecho_y_nace_pendiente()
    {
        var suscripcion = Suscripcion();
        var envio = Envio();
        var evento = Guid.NewGuid();
        var entrega = EntregaAviso.Crear(suscripcion, evento, envio, "EnTransito", TipoEventoAviso.CambioDeEstado, Ahora);

        Assert.Equal(EstadoEntregaAviso.Pendiente, entrega.Estado);
        Assert.Equal((Operador, Comercio), (entrega.OperadorId, entrega.ComercioId));
        Assert.Equal(suscripcion.Id, entrega.SuscripcionAvisoId);
        Assert.Equal(evento, entrega.EventoId);
        Assert.Equal(envio.Id, entrega.EnvioId);
        Assert.Equal("ENV-000123", entrega.NumeroEnvio);
        Assert.Equal("EnTransito", entrega.EstadoEnvio);
        Assert.Equal(Ahora, entrega.FechaEvento);
        Assert.Empty(entrega.Intentos);
    }

    [Fact]
    public void Crear_rechaza_otro_comercio_un_tipo_no_elegido_o_datos_vacios()
    {
        Assert.Throws<DomainException>(() => EntregaAviso.Crear(Suscripcion(), Guid.NewGuid(), Envio(Guid.NewGuid()),
            "Entregado", TipoEventoAviso.CambioDeEstado, Ahora));
        Assert.Throws<DomainException>(() => EntregaAviso.Crear(Suscripcion(TipoEventoAviso.Devolucion), Guid.NewGuid(),
            Envio(), "Reprogramado", TipoEventoAviso.EntregaFallida, Ahora));
        Assert.Throws<DomainException>(() => EntregaAviso.Crear(Suscripcion(), Guid.Empty, Envio(),
            "Entregado", TipoEventoAviso.CambioDeEstado, Ahora));
        Assert.Throws<DomainException>(() => EnvioSeguido.Crear(Operador, Guid.Empty, Guid.NewGuid(), "ENV-1"));
    }

    [Fact]
    public void Los_intentos_fallidos_se_numeran_y_el_exito_cierra_la_entrega()
    {
        var entrega = Nueva();
        entrega.RegistrarIntento(Ahora, false, 500, "Error del receptor.", ultimo: false);
        Assert.Equal(EstadoEntregaAviso.Fallida, entrega.Estado);
        entrega.RegistrarIntento(Ahora.AddSeconds(5), true, 204, null, ultimo: false);

        Assert.Equal(EstadoEntregaAviso.Exitosa, entrega.Estado);
        Assert.Equal([1, 2], entrega.Intentos.Select(i => i.Numero));
        Assert.Equal([500, 204], entrega.Intentos.Select(i => i.CodigoRespuesta));
        Assert.All(entrega.Intentos, i => Assert.Equal(entrega.Id, i.EntregaAvisoId));
        Assert.Throws<DomainException>(() => entrega.RegistrarIntento(Ahora, true, 200, null, ultimo: false));
    }

    [Fact]
    public void El_ultimo_fallo_va_a_la_cola_de_fallidos_y_reintentar_la_vuelve_pendiente()
    {
        var entrega = Nueva();
        entrega.RegistrarIntento(Ahora, false, null, new string('x', 900), ultimo: true);

        Assert.Equal(EstadoEntregaAviso.EnColaDeFallidos, entrega.Estado);
        Assert.Equal(IntentoAviso.LargoError, entrega.Intentos.Single().Error!.Length);
        Assert.Throws<DomainException>(() => entrega.RegistrarIntento(Ahora, true, 200, null, ultimo: false));

        entrega.Reintentar();
        Assert.Equal(EstadoEntregaAviso.Pendiente, entrega.Estado);
        Assert.Throws<DomainException>(entrega.Reintentar);
    }

    [Theory]
    [InlineData(EnvioAdmitido.Tipo, "Admitido", false, true)]
    [InlineData(EnvioAsignadoARuta.Tipo, "AsignadoARuta", false, false)]
    [InlineData(EnvioDesasignadoDeRuta.Tipo, "EnDeposito", false, false)]
    [InlineData(EnvioEntregado.Tipo, "Entregado", false, false)]
    [InlineData(EnvioReintegradoADeposito.Tipo, "EnDeposito", false, false)]
    [InlineData(EnvioRecibidoEnDeposito.Tipo, "EnDeposito", false, false)]
    public void El_consumidor_traduce_cada_evento_de_Envios_a_un_hecho(string tipo, string estado, bool fallido, bool alta)
    {
        var envio = Guid.NewGuid();
        var mensaje = Mensaje(tipo, new { EnvioId = envio, Numero = "ENV-9" });
        var hecho = LectorHechosEnvio.Leer(mensaje);

        Assert.Equal((mensaje.Id, envio, estado, fallido, alta), (hecho.EventoId, hecho.EnvioId, hecho.EstadoEnvio,
            hecho.IntentoFallido, hecho.EsAlta));
        Assert.Equal(mensaje.OcurridoEn, hecho.FechaEvento);
        Assert.Contains(tipo, PrepararAvisos.Tipos);
        // Todo cambio de estado, salvo el alta, borra el seguimiento público de la caché (CU-60).
        if (!alta) Assert.Contains(tipo, InvalidarSeguimiento.Tipos);
    }

    [Fact]
    public void El_intento_fallido_lleva_el_estado_final_y_un_evento_incompleto_es_invalido()
    {
        var hecho = LectorHechosEnvio.Leer(Mensaje(IntentoFallidoRegistrado.Tipo,
            new { EnvioId = Guid.NewGuid(), Numero = "ENV-9", EstadoFinal = "EnDevolucion" }));
        Assert.Equal("EnDevolucion", hecho.EstadoEnvio);
        Assert.True(hecho.IntentoFallido);

        Assert.Throws<InvalidDataException>(() => LectorHechosEnvio.Leer(Mensaje(IntentoFallidoRegistrado.Tipo,
            new { EnvioId = Guid.NewGuid() })));
        Assert.Throws<InvalidDataException>(() => LectorHechosEnvio.Leer(Mensaje(EnvioEntregado.Tipo, new { Numero = "ENV-9" })));
        Assert.Throws<InvalidDataException>(() => LectorHechosEnvio.Leer(Mensaje("OtroEvento.v1", new { EnvioId = Guid.NewGuid() })));
    }

    private static MensajeIntegracion Mensaje(string tipo, object contenido) =>
        new(Guid.NewGuid(), tipo, Operador, null, Ahora, null, JsonSerializer.Serialize(contenido));
}

using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.Modules.Seguimiento.Domain.Indicadores;
using Logistica.Modules.Seguimiento.Infrastructure.Messaging;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Seguimiento;

// CU-71: cada entrega o intento fallido es una fila de la tabla de lectura de los indicadores.
public class RegistroCumplimientoTests
{
    private static readonly DateTimeOffset Limite = new(2026, 11, 6, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid Zona = Guid.NewGuid();

    private static EnvioSeguido Envio(DateTimeOffset? limite = null, bool esPrueba = false) =>
        EnvioSeguido.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ENV-71", zonaId: Zona,
            entregaComprometidaHasta: limite, esPrueba: esPrueba);

    [Fact]
    public void La_entrega_antes_del_limite_es_a_tiempo_y_copia_zona_comercio_y_repartidor()
    {
        var envio = Envio(Limite);
        var repartidor = Guid.NewGuid();

        var registro = RegistroCumplimiento.DeEntrega(envio, repartidor, Limite);

        Assert.True(registro.ATiempo);
        Assert.Equal(TipoRegistroCumplimiento.Entrega, registro.Tipo);
        Assert.Equal((envio.OperadorId, envio.ComercioId, envio.Id, (Guid?)Zona, (Guid?)repartidor),
            (registro.OperadorId, registro.ComercioId, registro.EnvioId, registro.ZonaId, registro.RepartidorId));
        Assert.Null(registro.Motivo);
    }

    [Fact]
    public void La_entrega_despues_del_limite_no_es_a_tiempo_aunque_venga_en_otra_zona_horaria()
    {
        // 15:01 en Montevideo (UTC-3) son las 18:01 UTC: un minuto tarde.
        var entregadoEn = new DateTimeOffset(2026, 11, 6, 15, 1, 0, TimeSpan.FromHours(-3));

        var registro = RegistroCumplimiento.DeEntrega(Envio(Limite), null, entregadoEn);

        Assert.False(registro.ATiempo);
        Assert.Equal(TimeSpan.Zero, registro.OcurridoEn.Offset);
    }

    [Fact]
    public void Sin_ventana_comprometida_no_se_sabe_si_fue_a_tiempo()
    {
        Assert.Null(RegistroCumplimiento.DeEntrega(Envio(), Guid.NewGuid(), Limite).ATiempo);
    }

    [Fact]
    public void El_intento_fallido_guarda_el_motivo_y_no_cuenta_como_entrega()
    {
        var registro = RegistroCumplimiento.DeIntentoFallido(Envio(Limite), " Ausente ", Limite);

        Assert.Equal(TipoRegistroCumplimiento.IntentoFallido, registro.Tipo);
        Assert.Equal("Ausente", registro.Motivo);
        Assert.Null(registro.ATiempo);
        Assert.Throws<DomainException>(() => RegistroCumplimiento.DeIntentoFallido(Envio(Limite), " ", Limite));
    }

    [Fact]
    public void Los_envios_de_prueba_no_cuentan()
    {
        var prueba = Envio(Limite, esPrueba: true);

        Assert.Throws<DomainException>(() => RegistroCumplimiento.DeEntrega(prueba, null, Limite));
        Assert.Throws<DomainException>(() => RegistroCumplimiento.DeIntentoFallido(prueba, "Ausente", Limite));
    }

    [Fact]
    public void El_lector_toma_zona_ventana_repartidor_y_motivo_de_los_eventos()
    {
        var (envioId, repartidor) = (Guid.NewGuid(), Guid.NewGuid());

        var alta = LectorHechosEnvio.Leer(Mensaje(EnvioAdmitido.Tipo,
            new EnvioAdmitido(envioId, "ENV-71", Limite, ZonaId: Zona, EntregaComprometidaHasta: Limite)));
        var entrega = LectorHechosEnvio.Leer(Mensaje(EnvioEntregado.Tipo, new EnvioEntregado(envioId, "ENV-71", Limite, repartidor)));
        var fallo = LectorHechosEnvio.Leer(Mensaje(IntentoFallidoRegistrado.Tipo,
            new IntentoFallidoRegistrado(envioId, "ENV-71", 1, "Ausente", "Reprogramado", Limite, null)));

        Assert.Equal(((Guid?)Zona, (DateTimeOffset?)Limite), (alta.ZonaId, alta.EntregaComprometidaHasta));
        Assert.Equal(repartidor, entrega.ResponsableId);
        Assert.Equal("Ausente", fallo.Motivo);
    }

    [Fact]
    public void Un_alta_publicada_antes_de_agregar_la_zona_se_lee_sin_ella()
    {
        var alta = LectorHechosEnvio.Leer(Mensaje(EnvioAdmitido.Tipo,
            new { EnvioId = Guid.NewGuid(), Numero = "ENV-VIEJO", OcurridoEn = Limite }));

        Assert.Null(alta.ZonaId);
        Assert.Null(alta.EntregaComprometidaHasta);
    }

    private static MensajeIntegracion Mensaje(string tipo, object contenido) =>
        new(Guid.NewGuid(), tipo, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, null, JsonSerializer.Serialize(contenido));
}

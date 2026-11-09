using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.Modules.Seguimiento.Infrastructure.Messaging;

namespace Logistica.UnitTests.Seguimiento;

// CU-20 y CU-21: la devolución manual y el extravío llegan a los avisos al comercio (CU-64) con su estado y su tipo.
public class LectorHechosEnvioTests
{
    private static MensajeIntegracion Mensaje(string tipo, object contenido) =>
        new(Guid.NewGuid(), tipo, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, null, JsonSerializer.Serialize(contenido));

    [Fact]
    public void La_devolucion_manual_se_avisa_como_devolucion()
    {
        var envioId = Guid.NewGuid();
        var hecho = LectorHechosEnvio.Leer(Mensaje(DevolucionIniciada.Tipo,
            new DevolucionIniciada(envioId, "ENV-1", Guid.NewGuid(), "EnDeposito", "El cliente canceló", null, "Backoffice")));

        Assert.Equal(envioId, hecho.EnvioId);
        Assert.Equal("ENV-1", hecho.Numero);
        Assert.Equal("EnDevolucion", hecho.EstadoEnvio);
        Assert.Equal(TipoEventoAviso.Devolucion, ClasificadorAvisos.Clasificar(hecho.EstadoEnvio, hecho.IntentoFallido));
    }

    [Fact]
    public void El_extravio_se_avisa_como_cambio_de_estado()
    {
        var hecho = LectorHechosEnvio.Leer(Mensaje(EnvioExtraviado.Tipo,
            new EnvioExtraviado(Guid.NewGuid(), "ENV-2", "EnTransito", "Robo", null, "Backoffice")));

        Assert.Equal("Extraviado", hecho.EstadoEnvio);
        Assert.Equal(TipoEventoAviso.CambioDeEstado, ClasificadorAvisos.Clasificar(hecho.EstadoEnvio, hecho.IntentoFallido));
    }

    // RF 36: los hechos de bulto traen el bulto y el estado del envío; se clasifican por el estado del bulto.
    [Fact]
    public void Los_eventos_de_bulto_traen_el_bulto_y_se_clasifican_por_su_estado()
    {
        var envioId = Guid.NewGuid();
        var entregado = LectorHechosEnvio.Leer(Mensaje(BultoEntregado.Tipo,
            new BultoEntregado(envioId, "ENV-3", "ENV-3-1", "Reprogramado", DateTimeOffset.UtcNow)));
        var noEntregado = LectorHechosEnvio.Leer(Mensaje(BultoNoEntregado.Tipo,
            new BultoNoEntregado(envioId, "ENV-3", "ENV-3-2", "RECHAZADO", "EnDevolucion", "Reprogramado", DateTimeOffset.UtcNow)));
        var devuelto = LectorHechosEnvio.Leer(Mensaje(BultoDevuelto.Tipo,
            new BultoDevuelto(envioId, "ENV-3", "ENV-3-2", "Devuelto", DateTimeOffset.UtcNow)));

        Assert.Equal(("Reprogramado", new BultoAviso("ENV-3-1", "Entregado")), (entregado.EstadoEnvio, entregado.Bulto));
        Assert.Equal(new BultoAviso("ENV-3-2", "EnDevolucion", "RECHAZADO"), noEntregado.Bulto);
        Assert.True(noEntregado.IntentoFallido);
        Assert.Equal(new BultoAviso("ENV-3-2", "Devuelto"), devuelto.Bulto);
        Assert.Equal(TipoEventoAviso.CambioDeEstado, ClasificadorAvisos.Clasificar(entregado.Bulto!.Estado, entregado.IntentoFallido));
        Assert.Equal(TipoEventoAviso.Devolucion, ClasificadorAvisos.Clasificar(noEntregado.Bulto!.Estado, noEntregado.IntentoFallido));
        // Cada uno tiene su cola de avisos y borra el seguimiento público de la caché.
        Assert.All(new[] { BultoEntregado.Tipo, BultoNoEntregado.Tipo, BultoDevuelto.Tipo }, tipo =>
        {
            Assert.Contains(tipo, PrepararAvisos.Tipos);
            Assert.Contains(tipo, InvalidarSeguimiento.Tipos);
        });
    }

    // RF 36: una devolución con bultos ya entregados es un cierre mixto. Un evento anterior al campo no lo es.
    [Fact]
    public void La_devolucion_con_bultos_entregados_es_un_cierre_mixto()
    {
        var mixto = LectorHechosEnvio.Leer(Mensaje(EnvioDevuelto.Tipo,
            new EnvioDevuelto(Guid.NewGuid(), "ENV-4", DateTimeOffset.UtcNow, null, BultosEntregados: 2)));
        var anterior = LectorHechosEnvio.Leer(Mensaje(EnvioDevuelto.Tipo,
            new { EnvioId = Guid.NewGuid(), Numero = "ENV-5", OcurridoEn = DateTimeOffset.UtcNow }));

        Assert.Equal(("Devuelto", true), (mixto.EstadoEnvio, mixto.CierreMixto));
        Assert.Null(mixto.Bulto);
        Assert.False(anterior.CierreMixto);
    }

    // Sigue fallando con un tipo desconocido: el mensaje va a la cola de fallidos.
    [Fact]
    public void Un_tipo_desconocido_no_se_lee()
    {
        Assert.Throws<InvalidDataException>(() => LectorHechosEnvio.Leer(Mensaje("BultoExtraviado.v1",
            new { EnvioId = Guid.NewGuid(), Numero = "ENV-6", CodigoBulto = "ENV-6-1" })));
    }
}

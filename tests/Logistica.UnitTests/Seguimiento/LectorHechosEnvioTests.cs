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
}

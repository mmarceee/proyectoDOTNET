using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Ejecucion.Contracts.Events;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Seguimiento.Infrastructure.Messaging;

namespace Logistica.UnitTests.Seguimiento;

// CU-66, paso 3: qué ve el despachador de cada hecho, y cuáles son alertas operativas.
public class NovedadesTableroTests
{
    private static MensajeIntegracion Mensaje(string tipo, object contenido) =>
        new(Guid.NewGuid(), tipo, Guid.NewGuid(), null, DateTimeOffset.UtcNow, null, JsonSerializer.Serialize(contenido));

    [Fact]
    public void Una_entrega_es_una_novedad_y_no_una_alerta()
    {
        var novedad = NotificarNovedadTablero.Leer(Mensaje(EnvioEntregado.Tipo,
            new EnvioEntregado(Guid.NewGuid(), "ENV-000045", DateTimeOffset.UtcNow, null)));

        Assert.Equal("ENV-000045 se entregó.", novedad.Texto);
        Assert.False(novedad.Alerta);
    }

    // Entrega parcial (cierre técnico, D5): cada resultado de un bulto llega al tablero, aunque el resumen no cambie.
    [Fact]
    public void Los_resultados_de_cada_bulto_son_novedades_suscritas()
    {
        Assert.Contains(BultoEntregado.Tipo, NotificarNovedadTablero.Tipos);
        Assert.Contains(BultoNoEntregado.Tipo, NotificarNovedadTablero.Tipos);
        Assert.Contains(BultoDevuelto.Tipo, NotificarNovedadTablero.Tipos);

        var entregado = NotificarNovedadTablero.Leer(Mensaje(BultoEntregado.Tipo,
            new BultoEntregado(Guid.NewGuid(), "ENV-000045", "ENV-000045-2", "EnTransito", DateTimeOffset.UtcNow)));
        var noEntregado = NotificarNovedadTablero.Leer(Mensaje(BultoNoEntregado.Tipo,
            new BultoNoEntregado(Guid.NewGuid(), "ENV-000045", "ENV-000045-3", "AUSENTE", "Reprogramado", "Reprogramado",
                DateTimeOffset.UtcNow)));
        var devuelto = NotificarNovedadTablero.Leer(Mensaje(BultoDevuelto.Tipo,
            new BultoDevuelto(Guid.NewGuid(), "ENV-000045", "ENV-000045-3", "Devuelto", DateTimeOffset.UtcNow)));

        Assert.Equal("Se entregó el bulto ENV-000045-2 de ENV-000045.", entregado.Texto);
        Assert.Equal("No se entregó el bulto ENV-000045-3 de ENV-000045 (motivo AUSENTE).", noEntregado.Texto);
        Assert.Equal("El comercio recibió el bulto devuelto ENV-000045-3 de ENV-000045.", devuelto.Texto);
        Assert.False(entregado.Alerta || noEntregado.Alerta || devuelto.Alerta);
    }

    [Fact]
    public void Una_carga_con_diferencias_es_una_alerta_con_las_cantidades()
    {
        var novedad = NotificarNovedadTablero.Leer(Mensaje(CargaConDiscrepancias.Tipo,
            new CargaConDiscrepancias(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid(), Guid.NewGuid()], ["X-1"])));

        Assert.True(novedad.Alerta);
        Assert.Contains("2 envíos incompletos y 1 bultos sobrantes", novedad.Texto);
    }

    [Fact]
    public void Un_conflicto_de_sincronizacion_es_una_alerta_con_su_motivo()
    {
        var novedad = NotificarNovedadTablero.Leer(Mensaje(ConflictoSincronizacionRegistrado.Tipo,
            new ConflictoSincronizacionRegistrado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "Conflicto", "EnvioExtraviado")));

        Assert.True(novedad.Alerta);
        Assert.Contains("EnvioExtraviado", novedad.Texto);
    }
}

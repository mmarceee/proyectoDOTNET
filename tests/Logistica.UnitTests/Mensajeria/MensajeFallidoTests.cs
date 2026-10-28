using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.BuildingBlocks.Infrastructure.Messaging.Fallidos;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Mensajeria;

public class MensajeFallidoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly MensajeIntegracion Sobre = new(Guid.NewGuid(), "EnvioEntregado.v1", Guid.NewGuid(), Guid.NewGuid(),
        Ahora.AddMinutes(-5), "00-traza-01", "{\"EnvioId\":\"x\"}");

    private static MensajeFallido Nuevo() => MensajeFallido.Registrar(Sobre, "seguimiento.notificar-destinatario.EnvioEntregado.v1",
        4, "NotificacionNoEnviadaException", Ahora);

    [Fact]
    public void Registrar_copia_el_sobre_y_queda_pendiente_con_su_primer_error()
    {
        var fallido = Nuevo();

        Assert.Equal(EstadoMensajeFallido.Pendiente, fallido.Estado);
        Assert.Equal((Sobre.Id, Sobre.OperadorId, Sobre.ComercioId), (fallido.MessageId, fallido.OperadorId, fallido.ComercioId));
        Assert.Equal((4, "NotificacionNoEnviadaException"), (fallido.Intentos, fallido.UltimoError!));
        Assert.Single(fallido.Historial);
        // Se vuelve a publicar tal como llegó: mismo MessageId, inquilino, fecha, traza y contenido.
        Assert.Equal(Sobre, fallido.Sobre());
    }

    [Fact]
    public void Reintentar_registra_quien_y_un_nuevo_fallo_lo_devuelve_a_la_lista()
    {
        var fallido = Nuevo();
        fallido.MarcarReintentado("admin@operador-demo.uy", Ahora.AddMinutes(1));
        Assert.Equal((EstadoMensajeFallido.Reintentado, "admin@operador-demo.uy"), (fallido.Estado, fallido.ResueltoPor!));
        Assert.Throws<DomainException>(() => fallido.MarcarReintentado("otro", Ahora));

        // A3: el reintento volvió a agotar sus intentos.
        fallido.VolvioAFallar(4, "IOException", Ahora.AddMinutes(10));
        Assert.Equal(EstadoMensajeFallido.Pendiente, fallido.Estado);
        Assert.Null(fallido.ResueltoPor);
        Assert.Equal(2, fallido.Historial.Count);
        Assert.Equal("IOException", fallido.UltimoError);
    }

    [Fact]
    public void Descartar_exige_un_motivo_y_no_admite_otra_resolucion()
    {
        var fallido = Nuevo();
        Assert.Throws<DomainException>(() => fallido.Descartar("admin", "  ", Ahora));

        fallido.Descartar("admin", new string('m', 700), Ahora);
        Assert.Equal(EstadoMensajeFallido.Descartado, fallido.Estado);
        Assert.Equal(MensajeFallido.LargoMotivo, fallido.MotivoDescarte!.Length);
        Assert.Equal(Ahora, fallido.ResueltoEn);
        Assert.Throws<DomainException>(() => fallido.MarcarReintentado("admin", Ahora));
    }
}

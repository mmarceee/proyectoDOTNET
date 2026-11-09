using System.Text;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.Modules.Seguimiento.Infrastructure.Security;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Seguimiento;

// RF 36: avisos con contrato versionado. Un receptor v1 sigue recibiendo exactamente lo mismo que antes del cambio; uno
// v2 recibe además la versión y el bulto del hecho.
public class AvisosVersionadosTests
{
    private static readonly Guid Id = Guid.Parse("0192d6a1-0000-7000-8000-000000000001");
    private static readonly DateTimeOffset Fecha = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Operador = Guid.NewGuid();
    private static readonly Guid Comercio = Guid.NewGuid();

    // Prueba de compatibilidad: el contenido v1 es byte a byte el de antes de la versión (mismos campos, mismo orden, sin
    // version ni bulto), aunque el mensaje traiga un bulto.
    [Fact]
    public void El_contenido_v1_no_cambia()
    {
        var mensaje = new MensajeAviso(Id, "CambioDeEstado", Fecha, "ENV-1", "Entregado", false,
            Bulto: new BultoAviso("ENV-1-1", "Entregado"));

        var json = Encoding.UTF8.GetString(FirmadorAvisos.Serializar(mensaje));

        Assert.Equal("{\"id\":\"0192d6a1-0000-7000-8000-000000000001\",\"tipo\":\"CambioDeEstado\","
            + "\"fechaEvento\":\"2026-10-21T12:00:00+00:00\",\"numeroEnvio\":\"ENV-1\",\"estado\":\"Entregado\",\"prueba\":false}",
            json);
    }

    [Fact]
    public void El_contenido_v2_suma_la_version_y_el_bulto()
    {
        var conBulto = new MensajeAviso(Id, "EntregaFallida", Fecha, "ENV-1", "Reprogramado", false, 2,
            new BultoAviso("ENV-1-2", "EnDevolucion", "RECHAZADO"));
        var delEnvio = conBulto with { Bulto = null };

        Assert.Equal("{\"id\":\"0192d6a1-0000-7000-8000-000000000001\",\"version\":2,\"tipo\":\"EntregaFallida\","
            + "\"fechaEvento\":\"2026-10-21T12:00:00+00:00\",\"numeroEnvio\":\"ENV-1\",\"estado\":\"Reprogramado\","
            + "\"bulto\":{\"codigo\":\"ENV-1-2\",\"estado\":\"EnDevolucion\",\"motivo\":\"RECHAZADO\"},\"prueba\":false}",
            Encoding.UTF8.GetString(FirmadorAvisos.Serializar(conBulto)));
        Assert.Contains("\"bulto\":null", Encoding.UTF8.GetString(FirmadorAvisos.Serializar(delEnvio)));
    }

    // El mismo mensaje se serializa siempre igual: un reintento o un reenvío firma el mismo cuerpo.
    [Fact]
    public void El_mismo_mensaje_produce_el_mismo_cuerpo_y_la_misma_firma()
    {
        var mensaje = new MensajeAviso(Id, "CambioDeEstado", Fecha, "ENV-1", "Entregado", false, 2,
            new BultoAviso("ENV-1-1", "Entregado"));
        var secreto = Convert.ToBase64String(new byte[32]);
        var fecha = FirmadorAvisos.Fecha(Fecha);

        var (primero, segundo) = (FirmadorAvisos.Serializar(mensaje), FirmadorAvisos.Serializar(mensaje with { }));

        Assert.Equal(primero, segundo);
        Assert.Equal(FirmadorAvisos.Firmar(primero, fecha, secreto), FirmadorAvisos.Firmar(segundo, fecha, secreto));
        Assert.StartsWith("v1=", FirmadorAvisos.Firmar(primero, fecha, secreto));
    }

    [Fact]
    public void La_version_es_1_por_omision_y_solo_admite_1_o_2()
    {
        Assert.Equal(1, Suscripcion().Version);
        Assert.Equal(2, Suscripcion(2).Version);
        Assert.Throws<DomainException>(() => Suscripcion(3));
        Assert.Throws<DomainException>(() => Suscripcion(0));
    }

    // v1 no recibe hechos de bulto ni el cierre mixto; v2 recibe todo lo que pidió por tipo.
    [Theory]
    [InlineData(1, false, false, true)]
    [InlineData(1, true, false, false)]
    [InlineData(1, false, true, false)]
    [InlineData(2, false, false, true)]
    [InlineData(2, true, false, true)]
    [InlineData(2, false, true, true)]
    public void Cada_version_recibe_sus_hechos(int version, bool esDeBulto, bool cierreMixto, bool recibe)
    {
        Assert.Equal(recibe, Suscripcion(version).Recibe(TipoEventoAviso.Devolucion, esDeBulto, cierreMixto));
    }

    [Fact]
    public void Una_entrega_guarda_la_version_y_el_bulto_y_v1_no_acepta_bultos()
    {
        var envio = EnvioSeguido.Crear(Operador, Comercio, Guid.NewGuid(), "ENV-1");
        var bulto = new BultoAviso("ENV-1-1", "Devuelto");

        var entrega = EntregaAviso.Crear(Suscripcion(2), Guid.NewGuid(), envio, "Devuelto", TipoEventoAviso.Devolucion,
            Fecha, bulto: bulto);

        Assert.Equal((2, bulto), (entrega.Version, entrega.Bulto));
        Assert.Null(EntregaAviso.Crear(Suscripcion(), Guid.NewGuid(), envio, "Devuelto", TipoEventoAviso.Devolucion, Fecha).Bulto);
        Assert.Throws<DomainException>(() => EntregaAviso.Crear(Suscripcion(), Guid.NewGuid(), envio, "Devuelto",
            TipoEventoAviso.Devolucion, Fecha, bulto: bulto));
    }

    private static SuscripcionAviso Suscripcion(int version = SuscripcionAviso.VersionInicial) =>
        SuscripcionAviso.Crear(Operador, Comercio, Guid.NewGuid(), "https://comercio.example/avisos",
            [TipoEventoAviso.CambioDeEstado], "secreto-cifrado", version);
}

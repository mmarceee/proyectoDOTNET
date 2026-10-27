using Logistica.Modules.Envios.Domain.Devoluciones;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Envios;

// CU-31 (T15) y CU-55: la devolución vuelve al depósito y se cierra al entregarla al comercio.
public class DevolucionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 27, 12, 0, 0, TimeSpan.FromHours(-3));

    private static Devolucion Nueva()
    {
        var envio = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "ENV-1",
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, null, Ahora.AddDays(-2));
        return Devolucion.Crear(envio, "Intento fallido: RECHAZADO.", Ahora.AddDays(-1));
    }

    [Fact]
    public void Volver_al_deposito_la_deja_recibida_y_repetirlo_no_cambia_la_fecha()
    {
        var devolucion = Nueva();

        devolucion.MarcarRecibida(Ahora);
        devolucion.MarcarRecibida(Ahora.AddHours(2));

        Assert.Equal(EstadoDevolucion.RecibidaEnDeposito, devolucion.Estado);
        Assert.Equal(Ahora.ToUniversalTime(), devolucion.RecibidaEnDepositoEn);
    }

    [Fact]
    public void Cerrar_registra_quien_la_recibio_por_el_comercio()
    {
        var devolucion = Nueva();
        devolucion.MarcarRecibida(Ahora);

        devolucion.Cerrar("  María Gómez ", " 1.234.567-8 ", Ahora.AddHours(1));

        Assert.Equal(EstadoDevolucion.Cerrada, devolucion.Estado);
        Assert.Equal(("María Gómez", "1.234.567-8"), (devolucion.NombreReceptor!, devolucion.DocumentoReceptor!));
        Assert.Equal(Ahora.AddHours(1).ToUniversalTime(), devolucion.EntregadaAlComercioEn);
        Assert.Throws<DomainException>(() => devolucion.Cerrar("Otra", "1", Ahora));
        Assert.Throws<DomainException>(() => devolucion.MarcarRecibida(Ahora));
    }

    [Fact]
    public void No_se_entrega_al_comercio_lo_que_todavia_no_volvio_al_deposito()
    {
        var devolucion = Nueva();

        var error = Assert.Throws<DomainException>(() => devolucion.Cerrar("María Gómez", "12345678", Ahora));
        Assert.Contains("todavía no volvió al depósito", error.Message);
        Assert.Equal(EstadoDevolucion.Pendiente, devolucion.Estado);
    }

    [Theory]
    [InlineData("", "12345678")]
    [InlineData("María Gómez", " ")]
    public void El_nombre_y_el_documento_de_quien_recibe_son_obligatorios(string nombre, string documento)
    {
        var devolucion = Nueva();
        devolucion.MarcarRecibida(Ahora);

        Assert.Throws<DomainException>(() => devolucion.Cerrar(nombre, documento, Ahora));
        Assert.Throws<DomainException>(() => devolucion.Cerrar("María Gómez", new string('1', Devolucion.LargoDocumento + 1), Ahora));
        Assert.Equal(EstadoDevolucion.RecibidaEnDeposito, devolucion.Estado);
    }
}

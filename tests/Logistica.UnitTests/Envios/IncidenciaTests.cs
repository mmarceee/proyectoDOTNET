using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Domain.Incidencias;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Envios;

// CU-22: el ciclo de una incidencia (Abierta → En proceso → Resuelta) y sus reglas.
public class IncidenciaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    private static Incidencia NuevaIncidencia()
    {
        var envio = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "ENV-1", new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(PesoKg: 2, LargoCm: 30, AnchoCm: 20, AltoCm: 10, MontoTarifa: 150)],
            OrigenEvento.Backoffice, responsableId: null, Ahora);
        return Incidencia.Crear(envio, TipoIncidencia.Reclamo, "El comercio reclama la demora", Ahora);
    }

    [Fact]
    public void Una_incidencia_nueva_queda_abierta_sin_resolucion()
    {
        var incidencia = NuevaIncidencia();

        Assert.Equal(EstadoIncidencia.Abierta, incidencia.Estado);
        Assert.Null(incidencia.Resolucion);
        Assert.Null(incidencia.ResueltaEn);
    }

    [Fact]
    public void Una_incidencia_abierta_pasa_a_en_proceso()
    {
        var incidencia = NuevaIncidencia();

        incidencia.PasarAEnProceso();

        Assert.Equal(EstadoIncidencia.EnProceso, incidencia.Estado);
    }

    [Fact]
    public void Una_incidencia_en_proceso_no_vuelve_a_pasar_a_en_proceso()
    {
        var incidencia = NuevaIncidencia();
        incidencia.PasarAEnProceso();

        Assert.Throws<DomainException>(incidencia.PasarAEnProceso);
    }

    [Fact]
    public void Resolver_guarda_la_resolucion_y_la_fecha()
    {
        var incidencia = NuevaIncidencia();
        incidencia.PasarAEnProceso();
        var despues = Ahora.AddHours(2);

        incidencia.Resolver("  Se le explicó la demora al comercio  ", despues);

        Assert.Equal(EstadoIncidencia.Resuelta, incidencia.Estado);
        Assert.Equal("Se le explicó la demora al comercio", incidencia.Resolucion);
        Assert.Equal(despues, incidencia.ResueltaEn);
    }

    [Fact]
    public void Una_incidencia_abierta_se_puede_resolver_directamente()
    {
        var incidencia = NuevaIncidencia();

        incidencia.Resolver("Apareció el bulto", Ahora);

        Assert.Equal(EstadoIncidencia.Resuelta, incidencia.Estado);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void No_se_resuelve_sin_indicar_la_resolucion(string resolucion)
    {
        var incidencia = NuevaIncidencia();
        incidencia.PasarAEnProceso();

        Assert.Throws<DomainException>(() => incidencia.Resolver(resolucion, Ahora));
        Assert.Equal(EstadoIncidencia.EnProceso, incidencia.Estado);
        Assert.Null(incidencia.ResueltaEn);
    }

    [Fact]
    public void Una_incidencia_resuelta_no_se_vuelve_a_resolver_ni_pasa_a_en_proceso()
    {
        var incidencia = NuevaIncidencia();
        incidencia.Resolver("Resuelta", Ahora);

        Assert.Throws<DomainException>(() => incidencia.Resolver("Otra vez", Ahora.AddDays(1)));
        Assert.Throws<DomainException>(incidencia.PasarAEnProceso);
        Assert.Equal("Resuelta", incidencia.Resolucion);
        Assert.Equal(Ahora, incidencia.ResueltaEn);
    }
}

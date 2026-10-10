using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Planificacion;

public class PlanificarRutaTests
{
    private static readonly DateOnly Fecha = new(2035, 1, 1);
    private static VehiculoRuta Vehiculo(decimal peso = 100, decimal volumen = 1) => new(Guid.NewGuid(), peso, volumen, 100, 80, 60);
    private static EnvioRuta Envio(decimal peso = 2, decimal largo = 30, decimal ancho = 20, decimal alto = 10)
    {
        var id = Guid.NewGuid();
        return new(id, "ENV-TEST", null, null, null, false, [new(id, Guid.NewGuid(), peso, largo, ancho, alto)]);
    }
    private static Ruta Nueva() => Ruta.Crear(Guid.NewGuid(), Fecha, Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], DateTimeOffset.UtcNow);

    [Fact]
    public void Admite_rotacion_y_limites_exactos()
    {
        var evaluacion = PlanificadorDeRuta.Evaluar(Fecha, Vehiculo(100, .48m), 1, [Envio(100, 60, 100, 80)]);
        Assert.True(evaluacion.Valida);
        Assert.Equal(.48m, evaluacion.VolumenM3);
    }
    [Fact]
    public void Controla_peso_y_volumen_acumulados()
    {
        var evaluacion = PlanificadorDeRuta.Evaluar(Fecha, Vehiculo(3, .01m), 20, [Envio(), Envio()]);
        Assert.Equal(4, evaluacion.PesoKg);
        Assert.Contains(evaluacion.Restricciones, r => r.Codigo == "CapacidadPesoExcedida");
        Assert.Contains(evaluacion.Restricciones, r => r.Codigo == "CapacidadVolumenExcedida");
    }
    [Fact]
    public void Rechaza_un_bulto_que_no_entra_aunque_el_volumen_total_alcance()
    {
        var envio = Envio(largo: 101, ancho: 1, alto: 1);
        var evaluacion = PlanificadorDeRuta.Evaluar(Fecha, Vehiculo(), 20, [envio]);
        Assert.Contains(evaluacion.Restricciones, r => r.Codigo == "BultoNoAdmitido" && r.EnvioId == envio.Id);
    }
    [Fact]
    public void Controla_fecha_franja_y_maximo_de_paradas()
    {
        var zona = Guid.NewGuid();
        var envio = Envio() with { Fecha = Fecha.AddDays(1), ZonaId = zona, FranjaRequerida = true,
            Franja = new(Guid.NewGuid(), zona, new(9, 0), new(13, 0), [Fecha.AddDays(1).DayOfWeek]) };
        var evaluacion = PlanificadorDeRuta.Evaluar(Fecha, Vehiculo(), 1, [envio, Envio()]);
        Assert.Contains(evaluacion.Restricciones, r => r.Codigo == "FechaIncompatible");
        Assert.Contains(evaluacion.Restricciones, r => r.Codigo == "FranjaIncompatible");
        Assert.Contains(evaluacion.Restricciones, r => r.Codigo == "MaximoParadasExcedido");
    }
    [Fact]
    public void Admite_distintas_franjas_ofrecidas_el_mismo_dia()
    {
        var zona = Guid.NewGuid();
        var a = Envio() with { ZonaId = zona, FranjaRequerida = true, Franja = new(Guid.NewGuid(), zona, new(9, 0), new(13, 0), [Fecha.DayOfWeek]) };
        var b = Envio() with { ZonaId = zona, FranjaRequerida = true, Franja = new(Guid.NewGuid(), zona, new(14, 0), new(18, 0), [Fecha.DayOfWeek]) };
        Assert.True(PlanificadorDeRuta.Evaluar(Fecha, Vehiculo(), 20, [a, b]).Valida);
    }
    [Fact]
    public void No_crea_rutas_vacias_ni_agrega_duplicados_parcialmente()
    {
        Assert.Throws<DomainException>(() => Ruta.Crear(Guid.NewGuid(), Fecha, Guid.NewGuid(), Guid.NewGuid(), [], DateTimeOffset.UtcNow));
        var ruta = Nueva();
        Assert.Throws<DomainException>(() => ruta.AgregarEnvios([Guid.NewGuid(), ruta.Paradas[0].EnvioId]));
        Assert.Single(ruta.Paradas);
        Assert.Equal(1, ruta.Revision);
    }
    [Fact]
    public void Ruta_vacia_libera_recursos_y_al_agregar_los_reserva_nuevamente()
    {
        var ruta = Nueva();
        ruta.QuitarEnvio(ruta.Paradas[0].EnvioId);
        Assert.False(ruta.ReservaActiva);
        Assert.Equal(EstadoRuta.Planificada, ruta.Estado);
        Assert.Throws<DomainException>(() => ruta.Despachar(DateTimeOffset.UtcNow));
        ruta.AgregarEnvios([Guid.NewGuid()]);
        Assert.True(ruta.ReservaActiva);
        Assert.Equal(1, ruta.Paradas[0].Orden);
        Assert.Equal(3, ruta.Revision);
    }
    [Fact]
    public void En_curso_mantiene_reserva_y_no_admite_ediciones_hasta_finalizar()
    {
        var ruta = Nueva();
        ruta.Despachar(DateTimeOffset.UtcNow);
        ruta.Iniciar();
        Assert.True(ruta.ReservaActiva);
        Assert.Throws<DomainException>(() => ruta.ModificarPlanificacion(Fecha.AddDays(1), Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<DomainException>(() => ruta.AgregarEnvios([Guid.NewGuid()]));
        ruta.Finalizar();
        Assert.False(ruta.ReservaActiva);
    }
    [Fact]
    public void Evidencia_conserva_la_carga_aunque_se_quite_la_parada()
    {
        var envio = Envio();
        var ruta = Ruta.Crear(Guid.NewGuid(), Fecha, Guid.NewGuid(), Guid.NewGuid(), [envio.Id], DateTimeOffset.UtcNow);
        var evidencia = ValidacionRuta.Registrar(ruta, OperacionValidacionRuta.Creacion, Vehiculo(), Guid.NewGuid(), 20,
            [envio], null, DateTimeOffset.UtcNow);
        ruta.QuitarEnvio(envio.Id);
        Assert.Empty(ruta.Paradas);
        Assert.Equal(1, evidencia.RevisionRuta);
        Assert.Equal(envio.Bultos[0].PesoKg, Assert.Single(evidencia.Bultos).PesoKg);
        Assert.Equal(ruta.OperadorId, evidencia.Bultos[0].OperadorId);
    }
}

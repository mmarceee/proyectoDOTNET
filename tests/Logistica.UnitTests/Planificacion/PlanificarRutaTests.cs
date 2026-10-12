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
    private static Ruta Nueva() => Ruta.Crear(Guid.NewGuid(), Fecha, Guid.NewGuid(), Guid.NewGuid(), [new EnvioRuta(Guid.NewGuid(), "ENV-TEST", null, null, null, false, [])], DateTimeOffset.UtcNow);

    private static readonly PuntoEntrega Edificio = new("Av. Italia", "1234", "Montevideo", "Montevideo", "11300");
    private static EnvioRuta EnEdificio(int? desde = null, int? hasta = null)
        => Envio() with { Punto = Edificio, FranjaRequerida = desde is not null,
            Franja = desde is null ? null : new(Guid.NewGuid(), Guid.NewGuid(), new(desde.Value, 0), new(hasta!.Value, 0), [Fecha.DayOfWeek]) };

    [Fact]
    public void Tres_envios_al_mismo_edificio_cuentan_una_parada_y_toda_la_carga()
    {
        var envios = new[] { EnEdificio(), EnEdificio(), EnEdificio() };
        var evaluacion = PlanificadorDeRuta.Evaluar(Fecha, Vehiculo(), 1, envios);
        Assert.True(evaluacion.Valida); Assert.Equal(1, evaluacion.Paradas); Assert.Equal(6, evaluacion.PesoKg);
        var ruta = Ruta.Crear(Guid.NewGuid(), Fecha, Guid.NewGuid(), Guid.NewGuid(), envios, DateTimeOffset.UtcNow);
        Assert.Equal(3, Assert.Single(ruta.Paradas).Envios.Count);
    }

    [Fact]
    public void La_direccion_ignora_mayusculas_y_espacios_pero_distingue_numero_y_localidad()
    {
        var a = EnEdificio();
        var b = EnEdificio() with { Punto = new(" av.   ITALIA ", "1234", " montevideo ", "MONTEVIDEO", "11300") };
        Assert.Single(AgrupadorParadas.Agrupar([a, b]));
        Assert.Equal(2, AgrupadorParadas.Agrupar([a, b with { Punto = Edificio with { Numero = "1235" } }]).Count);
        Assert.Equal(2, AgrupadorParadas.Agrupar([a, b with { Punto = Edificio with { Localidad = "Otra localidad" } }]).Count);
    }

    [Fact]
    public void Manana_y_tarde_son_dos_visitas_y_el_envio_sin_franja_esta_en_una_sola()
    {
        var libre = EnEdificio(); var tarde = EnEdificio(14, 18); var manana = EnEdificio(9, 13);
        var grupos = AgrupadorParadas.Agrupar([libre, tarde, manana]);
        Assert.Equal(2, grupos.Count);
        Assert.Contains(grupos[0].Envios, e => e.Id == libre.Id);
        Assert.Equal(new TimeOnly(9, 0), grupos[0].Desde);
        Assert.Equal(1, grupos.SelectMany(g => g.Envios).Count(e => e.Id == libre.Id));
    }

    [Fact]
    public void Agrupa_solo_si_todos_comparten_un_intervalo_y_no_por_solapamientos_en_cadena()
    {
        var grupos = AgrupadorParadas.Agrupar([EnEdificio(9, 12), EnEdificio(11, 15), EnEdificio(14, 18)]);
        Assert.Equal(2, grupos.Count);
        Assert.Equal(new TimeOnly(11, 0), grupos[0].Desde); Assert.Equal(new TimeOnly(12, 0), grupos[0].Hasta);
        Assert.Equal(2, AgrupadorParadas.Agrupar([EnEdificio(9, 13), EnEdificio(13, 18)]).Count);
    }

    [Fact]
    public void Quitar_un_envio_conserva_la_parada_hasta_quitar_el_ultimo()
    {
        var a = EnEdificio(); var b = EnEdificio();
        var ruta = Ruta.Crear(Guid.NewGuid(), Fecha, Guid.NewGuid(), Guid.NewGuid(), [a, b], DateTimeOffset.UtcNow);
        var paradaId = ruta.Paradas[0].Id;
        ruta.QuitarEnvio(a.Id);
        Assert.Equal(paradaId, Assert.Single(ruta.Paradas).Id);
        Assert.Equal(b.Id, Assert.Single(ruta.Paradas[0].Envios).EnvioId); Assert.True(ruta.ReservaActiva);
        ruta.QuitarEnvio(b.Id); Assert.Empty(ruta.Paradas); Assert.False(ruta.ReservaActiva);
    }

    [Fact]
    public void Agregar_a_un_edificio_existente_conserva_la_parada_y_los_miembros_anteriores()
    {
        var a = EnEdificio(); var b = EnEdificio();
        var ruta = Ruta.Crear(Guid.NewGuid(), Fecha, Guid.NewGuid(), Guid.NewGuid(), [a], DateTimeOffset.UtcNow);
        var paradaId = ruta.Paradas[0].Id; var miembroId = ruta.Paradas[0].Envios[0].Id;
        ruta.AgregarEnvios([a, b]);
        Assert.Equal(paradaId, Assert.Single(ruta.Paradas).Id);
        Assert.Equal(miembroId, Assert.Single(ruta.Paradas[0].Envios, e => e.EnvioId == a.Id).Id);
        Assert.Equal(2, ruta.Revision);
    }

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
        Assert.Throws<DomainException>(() => ruta.AgregarEnvios([Envio(), Envio() with { Id = ruta.Paradas[0].Envios[0].EnvioId }, Envio() with { Id = ruta.Paradas[0].Envios[0].EnvioId }]));
        Assert.Single(ruta.Paradas);
        Assert.Equal(1, ruta.Revision);
    }
    [Fact]
    public void Ruta_vacia_libera_recursos_y_al_agregar_los_reserva_nuevamente()
    {
        var ruta = Nueva();
        ruta.QuitarEnvio(ruta.Paradas[0].Envios[0].EnvioId);
        Assert.False(ruta.ReservaActiva);
        Assert.Equal(EstadoRuta.Planificada, ruta.Estado);
        Assert.Throws<DomainException>(() => ruta.Despachar(DateTimeOffset.UtcNow));
        ruta.AgregarEnvios([new EnvioRuta(Guid.NewGuid(), "ENV-TEST", null, null, null, false, [])]);
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
        Assert.Throws<DomainException>(() => ruta.AgregarEnvios([new EnvioRuta(Guid.NewGuid(), "ENV-TEST", null, null, null, false, [])]));
        ruta.Finalizar();
        Assert.False(ruta.ReservaActiva);
    }
    [Fact]
    public void Evidencia_conserva_la_carga_aunque_se_quite_la_parada()
    {
        var envio = Envio();
        var ruta = Ruta.Crear(Guid.NewGuid(), Fecha, Guid.NewGuid(), Guid.NewGuid(), [envio], DateTimeOffset.UtcNow);
        var evidencia = ValidacionRuta.Registrar(ruta, OperacionValidacionRuta.Creacion, Vehiculo(), Guid.NewGuid(), 20,
            [envio], null, DateTimeOffset.UtcNow);
        ruta.QuitarEnvio(envio.Id);
        Assert.Empty(ruta.Paradas);
        Assert.Equal(1, evidencia.RevisionRuta);
        Assert.Equal(envio.Bultos[0].PesoKg, Assert.Single(evidencia.Bultos).PesoKg);
        Assert.Equal(ruta.OperadorId, evidencia.Bultos[0].OperadorId);
    }
}

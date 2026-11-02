using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Planificacion;

public class OrdenarParadasTests
{
    [Fact]
    public void Propuesta_prioriza_franja_zona_y_codigo_postal_con_empates_estables_y_sin_franja_al_final()
    {
        var ruta = Nueva(Envio(null), Envio(new(14, 0)), Envio(new(9, 0)), Envio(new(9, 0)), Envio(new(9, 0)), Envio(new(9, 0)));
        var p = ruta.Paradas.ToArray();
        var claves = new[] { new ClaveOrdenParada(p[0].Id, "A", "10000"), new(p[1].Id, "A", "10000"),
            new(p[2].Id, "Z", "10000"), new(p[3].Id, "a", "12000"), new(p[4].Id, "A", "11000"), new(p[5].Id, "A", "11000") };
        var revision = ruta.Revision;
        var propuesto = OrdenadorParadas.Proponer(ruta.Paradas, claves);
        Assert.Equal(new[] { p[4].Id, p[5].Id, p[3].Id, p[2].Id, p[1].Id, p[0].Id }, propuesto);
        Assert.Equal(revision, ruta.Revision);
        Assert.Equal(Enumerable.Range(1, 6), ruta.Paradas.Select(p => p.Orden));
    }

    [Fact]
    public void Ordenar_conserva_paradas_miembros_y_reservas_y_solo_incrementa_una_revision()
    {
        var a = Envio(new(9, 0));
        var otroEnEdificio = a with { Id = Guid.NewGuid(), Numero = "Otro apartamento" };
        var ruta = Nueva(a, otroEnEdificio, Envio(new(14, 0)));
        Assert.Equal(2, ruta.Paradas.Count);
        var miembros = ruta.Paradas.ToDictionary(p => p.Id, p => p.Envios.Select(e => e.Id).ToArray());
        var revision = ruta.Revision;
        var orden = ruta.Paradas.Select(p => p.Id).Reverse().ToArray();
        ruta.OrdenarParadas(orden);
        Assert.Equal(orden, ruta.Paradas.OrderBy(p => p.Orden).Select(p => p.Id));
        Assert.Equal(revision + 1, ruta.Revision);
        Assert.True(ruta.ReservaActiva);
        Assert.All(ruta.Paradas, p => Assert.Equal(miembros[p.Id], p.Envios.Select(e => e.Id)));
        ruta.OrdenarParadas(orden);
        Assert.Equal(revision + 1, ruta.Revision);
    }

    [Theory]
    [InlineData("duplicada")]
    [InlineData("faltante")]
    [InlineData("ajena")]
    public void Permutaciones_invalidas_no_cambian_orden_ni_revision(string caso)
    {
        var ruta = Nueva(Envio(null), Envio(null));
        var original = ruta.Paradas.Select(p => p.Id).ToArray();
        Guid[] pedido = caso switch { "duplicada" => [original[0], original[0]], "faltante" => [original[0]], _ => [original[0], Guid.NewGuid()] };
        var revision = ruta.Revision;
        Assert.Throws<DomainException>(() => ruta.OrdenarParadas(pedido));
        Assert.Equal(revision, ruta.Revision);
        Assert.Equal(original, ruta.Paradas.OrderBy(p => p.Orden).Select(p => p.Id));
    }

    [Fact]
    public void Mover_desplaza_las_intermedias_y_rechaza_posiciones_fuera_de_rango()
    {
        var ruta = Nueva(Envio(null), Envio(null), Envio(null));
        var p = ruta.Paradas.Select(p => p.Id).ToArray();
        ruta.MoverParada(p[2], 1);
        Assert.Equal(new[] { p[2], p[0], p[1] }, ruta.Paradas.OrderBy(p => p.Orden).Select(p => p.Id));
        Assert.Throws<DomainException>(() => ruta.MoverParada(p[1], 0));
        Assert.Throws<DomainException>(() => ruta.MoverParada(Guid.NewGuid(), 2));
        Assert.Equal(2, ruta.Revision);
    }

    [Fact]
    public void Despachada_y_vacia_no_se_pueden_ordenar()
    {
        var ruta = Nueva(Envio(null));
        ruta.Despachar(DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(() => ruta.OrdenarParadas(ruta.Paradas.Select(p => p.Id).ToArray()));
        var vacia = Nueva(Envio(null));
        vacia.QuitarEnvio(vacia.EnvioIds.Single());
        Assert.Throws<DomainException>(() => vacia.OrdenarParadas([]));
    }

    [Fact]
    public void Advertencia_se_basa_en_la_franja_efectiva_y_no_confunde_un_solapamiento_con_un_incumplimiento()
    {
        var ruta = Nueva(Envio(new(14, 0)), Envio(new(9, 0)), Envio(null));
        var aviso = Assert.Single(OrdenadorParadas.Advertir(ruta.Paradas));
        Assert.Equal(ruta.Paradas[1].Id, aviso.ParadaId);
        Assert.Equal(new TimeOnly(14, 0), aviso.HoraMinima);
        var solapadas = Nueva(Envio(new(10, 0)), Envio(new(9, 0)));
        Assert.Empty(OrdenadorParadas.Advertir(solapadas.Paradas));
    }

    private static Ruta Nueva(params EnvioRuta[] envios) => Ruta.Crear(Guid.NewGuid(), new(2040, 1, 1), Guid.NewGuid(), Guid.NewGuid(), envios, DateTimeOffset.UtcNow);
    private static EnvioRuta Envio(TimeOnly? desde)
    {
        var id = Guid.NewGuid();
        return new(id, id.ToString(), null, null, desde is TimeOnly hora
            ? new FranjaRuta(Guid.NewGuid(), Guid.NewGuid(), hora, hora.AddHours(3), Enum.GetValues<DayOfWeek>()) : null,
            desde is not null, [], new PuntoEntrega("Calle", id.ToString(), "Montevideo", "Montevideo", "11300"));
    }
}

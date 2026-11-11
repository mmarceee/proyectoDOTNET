using Logistica.Modules.Envios.Domain.Envios;

namespace Logistica.UnitTests.Envios;

public sealed class CargaParcialBultosTests
{
    [Fact]
    public void La_carga_parcial_mueve_solo_los_escaneados_y_entregarlos_no_cierra_el_envio()
    {
        var e = Crear(); var ids = e.Bultos.Take(2).Select(b => b.Id).ToArray();
        Assert.False(e.PuedeCargarBultos(ids, admiteParcial:false));
        Assert.True(e.PuedeCargarBultos(ids, admiteParcial:true));
        e.CargarBultos(ids,null,DateTimeOffset.UtcNow);
        Assert.Equal(EstadoEnvio.AsignadoARuta,e.Bultos[2].Estado);
        var lugar = new Ubicacion(-34.9m,-56.16m);
        var foto = new PruebaEntrega(Guid.NewGuid(),Guid.NewGuid(),null,null,lugar,DateTimeOffset.UtcNow);
        e.RegistrarVisita(ids.Select(id => new ResultadoBulto(id)).ToArray(),foto,null,null,lugar,
            new ReglasEntrega(false,false,3,TimeSpan.FromHours(24),true,admiteEntregaParcial:true),null,DateTimeOffset.UtcNow);
        Assert.Equal(ResumenEnvio.EntregadoParcialmente,e.Resumen); Assert.False(e.Terminado);
        Assert.NotEqual(EstadoEnvio.Entregado,e.Estado); Assert.Equal(0,e.Bultos[2].IntentosFallidos);
    }
    [Fact]
    public void No_se_entregan_solo_los_cargados_si_las_reglas_prohiben_la_parcial()
    {
        var e=Crear(); var ids=e.Bultos.Take(2).Select(b=>b.Id).ToArray(); e.CargarBultos(ids,null,DateTimeOffset.UtcNow);
        var lugar=new Ubicacion(-34.9m,-56.16m); var firma=new PruebaEntrega(Guid.NewGuid(),null,null,null,lugar,DateTimeOffset.UtcNow);
        Assert.Throws<Logistica.SharedKernel.DomainException>(()=>e.RegistrarVisita(ids.Select(id=>new ResultadoBulto(id)).ToArray(),firma,null,null,lugar,
            new ReglasEntrega(false,false,3,TimeSpan.FromHours(24),true,admiteEntregaParcial:false),null,DateTimeOffset.UtcNow));
        Assert.All(e.Bultos.Take(2),b=>Assert.Equal(EstadoEnvio.EnTransito,b.Estado));Assert.Empty(e.Intentos);
    }
    private static Envio Crear()
    {
        var ahora=DateTimeOffset.UtcNow;
        var e=Envio.Crear(Guid.NewGuid(),Guid.NewGuid(),"ENV-000001",new Destinatario("Ana","099123456"),
            new Direccion("Calle","1","Montevideo","Montevideo","11300"),Enumerable.Repeat(new DatosBulto(1,10,10,10,100),3).ToArray(),OrigenEvento.PortalComercio,null,ahora);
        e.Transicionar(EstadoEnvio.EnDeposito,OrigenEvento.Backoffice,null,ahora);
        e.Transicionar(EstadoEnvio.AsignadoARuta,OrigenEvento.Backoffice,null,ahora);return e;
    }
}

using System.Net;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Planificacion;

public sealed class ListadoRutasTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private HttpClient Cliente() => factory.WithWebHostBuilder(b =>
        b.UseSetting("Planificacion:HabilitarDesarrolloSinIdentity", "true")).CreateClient();
    private IServiceScope Scope()
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();
        return scope;
    }

    [Fact]
    public async Task Muestra_nombre_y_matricula_incluso_si_los_recursos_se_desactivaron_y_no_expone_otro_operador()
    {
        using var client = Cliente();
        (await client.GetAsync("/health")).EnsureSuccessStatusCode();
        var repartidorId = DatosIniciales.RepartidorDemoId;
        var vehiculoId = Guid.Parse("51111111-1111-1111-1111-111111111111");
        const string nombre = "Repartidor del listado";
        const string matricula = "LISTADO123";
        using (var scope = Scope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<AdministracionDbContext>();
            var repartidor = await admin.Repartidores.SingleAsync(r => r.Id == repartidorId);
            admin.Entry(repartidor).Property(r => r.Nombre).CurrentValue = nombre;
            var vehiculo = await admin.Vehiculos.SingleAsync(v => v.Id == vehiculoId);
            admin.Entry(vehiculo).Property(v => v.Matricula).CurrentValue = matricula;
            await admin.SaveChangesAsync();
            var plan = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
            plan.Rutas.Add(Ruta.Crear(DatosIniciales.OperadorDemoId, new DateOnly(2040, 6, 1), repartidorId,
                vehiculoId, [new EnvioRuta(Guid.NewGuid(), "ENV-LISTADO", null, null, null, false, [])], DateTimeOffset.UtcNow));
            await plan.SaveChangesAsync();
        }
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/backoffice/rutas?Fecha=2040-06-01"));
        Assert.Contains("<th>Repartidor</th>", html); Assert.Contains("<th>Vehículo</th>", html);
        Assert.Contains(nombre, html); Assert.Contains(matricula, html);
        using (var scope = Scope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<AdministracionDbContext>();
            admin.Entry(await admin.Repartidores.SingleAsync(r => r.Id == repartidorId)).Property(r => r.Activo).CurrentValue = false;
            admin.Entry(await admin.Vehiculos.SingleAsync(v => v.Id == vehiculoId)).Property(v => v.Activo).CurrentValue = false;
            await admin.SaveChangesAsync();
            var nombres = await scope.ServiceProvider.GetRequiredService<IAdministracionModuleApi>()
                .ConsultarNombresRecursosAsync([repartidorId, DatosIniciales.RepartidorEsteId],
                    [vehiculoId, Guid.Parse("53333333-3333-3333-3333-333333333333")], default);
            Assert.Equal(nombre, Assert.Single(nombres.Repartidores).Value);
            Assert.Equal(matricula, Assert.Single(nombres.Vehiculos).Value);
        }
        html = WebUtility.HtmlDecode(await client.GetStringAsync("/backoffice/rutas?Fecha=2040-06-01"));
        Assert.Contains(nombre, html); Assert.Contains(matricula, html);
        Assert.DoesNotContain("No asignado", html);
    }
}

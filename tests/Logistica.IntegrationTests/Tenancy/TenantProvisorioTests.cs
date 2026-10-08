using Logistica.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Tenancy;

// El inquilino provisorio del 8/10: operador fijo y comercio según la ruta (guía, sección 4).
// Se reemplaza el 15/10 por los claims de Identity; estas pruebas muestran qué comportamiento conservar.
public class TenantProvisorioTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    // Los de appsettings.json de la API, y los mismos que carga DatosIniciales.
    private static readonly Guid OperadorProvisorio = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ComercioProvisorio = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void En_el_backoffice_es_personal_del_operador_y_no_tiene_comercio()
    {
        var tenant = TenantPara("/backoffice/envios");

        Assert.Equal(OperadorProvisorio, tenant.OperadorId);
        Assert.Null(tenant.ComercioId);
    }

    [Fact]
    public void En_la_api_es_el_comercio_provisorio_de_ese_operador()
    {
        var tenant = TenantPara("/api/envios");

        Assert.Equal(OperadorProvisorio, tenant.OperadorId);
        Assert.Equal(ComercioProvisorio, tenant.ComercioId);
    }

    [Fact]
    public void Sin_request_no_tiene_comercio()
    {
        var tenant = TenantPara(null);

        Assert.Null(tenant.ComercioId);
    }

    // Simula un request a esa ruta (o ninguno, si es null) y le pide a la API el ICurrentTenant,
    // igual que lo recibiría un handler.
    private ICurrentTenant TenantPara(string? ruta)
    {
        var scope = factory.Services.CreateScope();

        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = ruta is null ? null : new DefaultHttpContext { Request = { Path = ruta } };

        return scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
    }
}

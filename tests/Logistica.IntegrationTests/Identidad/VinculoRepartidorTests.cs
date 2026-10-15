using System.Security.Claims;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Identidad;

// El usuario repartidor lleva en la cookie su Repartidor (claim repartidor_id), que necesita la sincronización para
// verificar que la ruta sea suya (ADR-0004, sección 2.3). Se arma la identidad tal como al iniciar sesión.
public class VinculoRepartidorTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Theory]
    [InlineData("repartidor@operador-demo.uy", "41111111-1111-1111-1111-111111111111")]
    [InlineData("repartidor@envios-del-este.uy", "43333333-3333-3333-3333-333333333333")]
    public async Task Cada_usuario_repartidor_lleva_su_repartidor_en_la_sesion(string email, string repartidorId)
    {
        var identidad = await IdentidadDeAsync(email);

        Assert.Equal(repartidorId, identidad.FindFirstValue(ClaimsDeSesion.RepartidorId));
    }

    [Fact]
    public async Task El_resto_del_personal_no_lleva_repartidor()
    {
        var identidad = await IdentidadDeAsync("despacho@operador-demo.uy");

        Assert.Null(identidad.FindFirst(ClaimsDeSesion.RepartidorId));
    }

    // Arma la identidad con la misma fábrica de claims que usa el inicio de sesión.
    private async Task<ClaimsPrincipal> IdentidadDeAsync(string email)
    {
        factory.CreateClient(); // arranca la API: migraciones y datos iniciales
        using var scope = factory.Services.CreateScope();
        var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var fabrica = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<Usuario>>();

        var usuario = await usuarios.FindByEmailAsync(email);
        return await fabrica.CreateAsync(usuario!);
    }
}

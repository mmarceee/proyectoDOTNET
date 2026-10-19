using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Seguimiento;

// CU-60, el contrato de Envíos para el seguimiento público: sólo lo que el destinatario puede ver, y nada de otro
// operador. Sin sesión, el inquilino de estas consultas es el provisorio (operador Demo).
public class SeguimientoDeEnvioTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task Muestra_nombre_de_pila_localidad_e_historial_sin_datos_del_personal()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId);

        using var scope = factory.Services.CreateScope();
        var seguimiento = await scope.ServiceProvider.GetRequiredService<IEnviosModuleApi>()
            .SeguimientoDeAsync(envio.Id, CancellationToken.None);

        Assert.NotNull(seguimiento);
        Assert.Equal("Ana", seguimiento.NombreDePila);
        Assert.Equal("Montevideo", seguimiento.Localidad);
        Assert.Equal("EnDeposito", seguimiento.Estado);
        Assert.Equal(["Admitido", "EnDeposito"], seguimiento.Historial.Select(h => h.Estado));

        // Ni la dirección, ni el teléfono, ni el documento, ni quién hizo cada cambio están en el resultado.
        var texto = System.Text.Json.JsonSerializer.Serialize(seguimiento);
        Assert.DoesNotContain("Italia", texto);
        Assert.DoesNotContain("099123456", texto);
        Assert.DoesNotContain("Pérez", texto);
        Assert.DoesNotContain(Responsable.ToString(), texto);
    }

    // Aislamiento: con el inquilino de Demo, un envío de Este no existe.
    [Fact]
    public async Task Un_envio_de_otro_operador_no_aparece()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorEsteId);

        using var scope = factory.Services.CreateScope();
        var seguimiento = await scope.ServiceProvider.GetRequiredService<IEnviosModuleApi>()
            .SeguimientoDeAsync(envio.Id, CancellationToken.None);

        Assert.Null(seguimiento);
    }

    private static readonly Guid Responsable = Guid.NewGuid();

    // Un envío admitido y recibido en el depósito por un responsable, guardado con el operador indicado como inquilino.
    private async Task<Envio> EnvioAsync(Guid operadorId)
    {
        factory.CreateClient(); // arranca la API: migraciones y datos iniciales
        var ahora = DateTimeOffset.UtcNow;
        var envio = Envio.Crear(operadorId, DatosIniciales.ComercioDemoId, $"ENV-S-{Guid.NewGuid():N}",
            new Destinatario("Ana Pérez", "099123456", documento: "12345678"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, Responsable, ahora.AddHours(-2));
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, Responsable, ahora.AddHours(-1));

        using var scope = factory.Services.CreateScope();
        await using var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider,
            new InquilinoFijo(operadorId, null));
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio;
    }
}

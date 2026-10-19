using System.Net;
using System.Net.Http.Json;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Seguimiento;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Seguimiento;

// CU-60 de punta a punta: GET /api/seguimiento/{token} sin sesión, con el respaldo provisorio apagado para que el único
// inquilino posible sea el del token.
public class SeguimientoPublicoTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private HttpClient Cliente(int consultasPorMinuto = 1000) => factory
        .WithWebHostBuilder(b => b
            .UseSetting("Identity:PermitirTenantProvisorio", "false")
            .UseSetting("Seguimiento:ConsultasPorMinuto", consultasPorMinuto.ToString()))
        .CreateClient(Sesion.Opciones);

    // Flujo principal y requerimiento 1: sin sesión, con la marca del operador y sin datos de terceros.
    [Fact]
    public async Task Con_un_enlace_valido_se_ve_el_envio_sin_datos_personales_ni_identificadores()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId);
        using var client = Cliente();

        var respuesta = await client.GetAsync($"/api/seguimiento/{Token(DatosIniciales.OperadorDemoId, envio.Id)}");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        var seguimiento = await respuesta.Content.ReadFromJsonAsync<SeguimientoPublicoResponse>();
        Assert.Equal("En el depósito", seguimiento!.Estado);
        Assert.Equal("Ana", seguimiento.NombreDePila);
        Assert.Equal("Operador Demo", seguimiento.Marca.Operador);
        Assert.Equal(["Recibimos tu envío", "En el depósito"], seguimiento.Historial.Select(h => h.Estado));
        Assert.DoesNotContain("Pérez", cuerpo);
        Assert.DoesNotContain("Italia", cuerpo);
        Assert.DoesNotContain("099123456", cuerpo);
        Assert.DoesNotContain(envio.Id.ToString(), cuerpo);
    }

    // A1 y A2: un token alterado y uno de un envío que no existe responden exactamente igual.
    [Fact]
    public async Task Un_enlace_alterado_y_uno_de_un_envio_inexistente_se_ven_igual()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId);
        using var client = Cliente();
        var token = Token(DatosIniciales.OperadorDemoId, envio.Id);
        var alterado = token[..10] + (token[10] == 'A' ? 'B' : 'A') + token[11..];

        var respuestaAlterado = await client.GetAsync($"/api/seguimiento/{alterado}");
        var respuestaInexistente = await client.GetAsync($"/api/seguimiento/{Token(DatosIniciales.OperadorDemoId, Guid.NewGuid())}");

        Assert.Equal(HttpStatusCode.NotFound, respuestaAlterado.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, respuestaInexistente.StatusCode);
        Assert.Contains("Enlace no válido", await respuestaAlterado.Content.ReadAsStringAsync());
        Assert.Contains("Enlace no válido", await respuestaInexistente.Content.ReadAsStringAsync());
    }

    // Aislamiento (ADR-0002): el inquilino es el del token. Un envío de Este con un token de Demo no existe.
    [Fact]
    public async Task El_inquilino_es_el_del_token()
    {
        var deEste = await EnvioAsync(DatosIniciales.OperadorEsteId);
        using var client = Cliente();

        var conOtroOperador = await client.GetAsync($"/api/seguimiento/{Token(DatosIniciales.OperadorDemoId, deEste.Id)}");
        var conSuOperador = await client.GetAsync($"/api/seguimiento/{Token(DatosIniciales.OperadorEsteId, deEste.Id)}");

        Assert.Equal(HttpStatusCode.NotFound, conOtroOperador.StatusCode);
        Assert.Equal(HttpStatusCode.OK, conSuOperador.StatusCode);
        Assert.Equal("Envíos del Este", (await conSuOperador.Content.ReadFromJsonAsync<SeguimientoPublicoResponse>())!.Marca.Operador);
    }

    // A3 (letra 6.3): superado el límite de consultas por IP, responde 429.
    [Fact]
    public async Task Demasiadas_consultas_seguidas_se_frenan()
    {
        using var client = Cliente(consultasPorMinuto: 3);
        var token = Token(DatosIniciales.OperadorDemoId, Guid.NewGuid());

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/seguimiento/{token}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync($"/api/seguimiento/{token}")).StatusCode);
    }

    // El personal obtiene el enlace desde el Backoffice; el de otro operador no encuentra el envío.
    [Fact]
    public async Task El_personal_obtiene_el_enlace_y_el_de_otro_operador_no()
    {
        var envio = await EnvioAsync(DatosIniciales.OperadorDemoId);
        using var demo = Cliente();
        await Sesion.IngresarAsync(demo, "admin@operador-demo.uy");
        using var este = Cliente();
        await Sesion.IngresarAsync(este, "admin@envios-del-este.uy");

        var pagina = await demo.GetStringAsync($"/backoffice/seguimiento/{envio.Numero}");

        Assert.Contains($"/seguimiento/{Token(DatosIniciales.OperadorDemoId, envio.Id)}", pagina);
        Assert.Equal(HttpStatusCode.NotFound, (await este.GetAsync($"/backoffice/seguimiento/{envio.Numero}")).StatusCode);
    }

    private string Token(Guid operadorId, Guid envioId) =>
        factory.Services.GetRequiredService<TokenSeguimiento>().Generar(operadorId, envioId);

    // Un envío admitido y recibido en el depósito, guardado con el operador indicado como inquilino.
    private async Task<Envio> EnvioAsync(Guid operadorId)
    {
        factory.CreateClient(); // arranca la API: migraciones y datos iniciales
        var ahora = DateTimeOffset.UtcNow;
        var envio = Envio.Crear(operadorId, DatosIniciales.ComercioDemoId, $"ENV-T-{Guid.NewGuid():N}".ToUpperInvariant(),
            new Destinatario("Ana Pérez", "099123456"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(2, 30, 20, 10, 150)], OrigenEvento.PortalComercio, null, ahora.AddHours(-2));
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora.AddHours(-1));

        using var scope = factory.Services.CreateScope();
        await using var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider,
            new InquilinoFijo(operadorId, null));
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio;
    }
}

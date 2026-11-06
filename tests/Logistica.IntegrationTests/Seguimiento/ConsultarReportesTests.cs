using System.Net;
using System.Text;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.Modules.Seguimiento.Domain.Indicadores;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Seguimiento;

// CU-72: los reportes salen de la tabla de lectura de los indicadores (CU-71), con los nombres de Administración. Los
// registros son de un día de 2020, así no se mezclan con los de otras pruebas que comparten la base.
public class ConsultarReportesTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly Guid ZonaDemo = Guid.Parse("61111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Dia = new(2020, 3, 10, 15, 0, 0, TimeSpan.Zero); // 12 h en Montevideo
    private const string Periodo = "Desde=2020-03-10&Hasta=2020-03-10";
    private static readonly SemaphoreSlim Sembrado = new(1, 1);
    private static bool _sembrado;

    private HttpClient Cliente() => factory
        .WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
        .CreateClient(Sesion.Opciones);

    [Fact]
    public async Task El_cumplimiento_por_zona_y_por_repartidor_muestra_el_porcentaje_a_tiempo()
    {
        await SembrarAsync();
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");

        var porZona = await despacho.GetStringAsync($"/backoffice/reportes?Tipo=Cumplimiento&Dimension=Zona&{Periodo}");
        var porRepartidor = await despacho.GetStringAsync($"/backoffice/reportes?Tipo=Cumplimiento&Dimension=Repartidor&{Periodo}");

        // Tres entregas con ventana, dos a tiempo (66,7 %), y un intento fallido.
        Assert.Contains("Montevideo Demo", porZona);
        Assert.Contains("66,7", porZona);
        Assert.Contains("Repartidor Demo 1", porRepartidor);
        Assert.Contains("href=\"/backoffice/reportes\"", await despacho.GetStringAsync("/backoffice"));
    }

    [Fact]
    public async Task Los_motivos_y_el_volumen_se_descargan_en_csv()
    {
        await SembrarAsync();
        using var admin = Cliente();
        await Sesion.IngresarAsync(admin, "admin@operador-demo.uy");

        var motivos = await admin.GetStringAsync($"/backoffice/reportes?Tipo=Motivos&{Periodo}");
        using var volumen = await admin.GetAsync($"/backoffice/reportes/csv?Tipo=Volumen&{Periodo}");

        Assert.Contains("Ausente", motivos);
        Assert.Contains("100,0", motivos);
        Assert.Equal(HttpStatusCode.OK, volumen.StatusCode);
        Assert.Equal("text/csv", volumen.Content.Headers.ContentType?.MediaType);
        var csv = Encoding.UTF8.GetString(await volumen.Content.ReadAsByteArrayAsync());
        Assert.Contains("Día;Altas;Entregas;Intentos fallidos", csv);
        Assert.Contains("10/03/2020;1;3;1", csv);
    }

    [Fact]
    public async Task Un_periodo_invertido_se_informa_y_otro_operador_no_ve_los_datos()
    {
        await SembrarAsync();
        using var despacho = Cliente();
        await Sesion.IngresarAsync(despacho, "despacho@operador-demo.uy");
        using var este = Cliente();
        await Sesion.IngresarAsync(este, "despacho@envios-del-este.uy");

        // A2.
        Assert.Contains("La fecha desde es posterior a la fecha hasta.",
            await despacho.GetStringAsync("/backoffice/reportes?Tipo=Volumen&Desde=2020-03-11&Hasta=2020-03-10"));
        // A1 y aislamiento (ADR-0002).
        var ajeno = await este.GetStringAsync($"/backoffice/reportes?Tipo=Motivos&{Periodo}");
        Assert.Contains("No hay datos para el período elegido.", ajeno);
        Assert.DoesNotContain("Ausente", ajeno);
    }

    [Fact]
    public async Task El_operario_de_deposito_no_ve_los_reportes()
    {
        using var deposito = Cliente();
        await Sesion.IngresarAsync(deposito, "deposito@operador-demo.uy");

        Assert.Equal(HttpStatusCode.Forbidden, (await deposito.GetAsync("/backoffice/reportes")).StatusCode);
    }

    // Una vez por corrida: un alta, tres entregas (una tarde) y un intento fallido del operador Demo.
    private async Task SembrarAsync()
    {
        await Sembrado.WaitAsync();
        try
        {
            if (_sembrado) return;
            await using var scope = factory.Services.CreateAsyncScope();
            await using var db = ActivatorUtilities.CreateInstance<SeguimientoDbContext>(scope.ServiceProvider,
                new InquilinoFijo(DatosIniciales.OperadorDemoId, null));
            EnvioSeguido Envio(DateTimeOffset limite) => EnvioSeguido.Crear(DatosIniciales.OperadorDemoId,
                DatosIniciales.ComercioDemoId, Guid.NewGuid(), "ENV-CU72", zonaId: ZonaDemo, entregaComprometidaHasta: limite);
            var repartidor = DatosIniciales.RepartidorDemoId;

            db.RegistrosCumplimiento.AddRange(
                RegistroCumplimiento.DeAlta(Envio(Dia.AddDays(1)), Dia),
                RegistroCumplimiento.DeEntrega(Envio(Dia.AddHours(1)), repartidor, Dia),
                RegistroCumplimiento.DeEntrega(Envio(Dia.AddHours(1)), repartidor, Dia),
                RegistroCumplimiento.DeEntrega(Envio(Dia.AddHours(-1)), repartidor, Dia),
                RegistroCumplimiento.DeIntentoFallido(Envio(Dia.AddHours(1)), repartidor, "Ausente", Dia));
            await db.SaveChangesAsync();
            _sembrado = true;
        }
        finally
        {
            Sembrado.Release();
        }
    }
}

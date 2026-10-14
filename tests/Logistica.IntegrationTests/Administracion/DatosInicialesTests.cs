using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Modules.Administracion;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Logistica.IntegrationTests.Administracion;

// Datos iniciales (ADR-0002, sección 2.8): al iniciar, la API deja cargados dos operadores con configuración distinta
// que trabajan con el mismo comercio, y volver a ejecutarlos no duplica nada.
public class DatosInicialesTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    // Los de appsettings.json de la API: TenantProvisorio y DatosIniciales tienen que coincidir.
    private static readonly Guid OperadorDemo = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ComercioDemo = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // El segundo operador (15/10).
    private static readonly Guid OperadorEste = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static TheoryData<Guid> Operadores => [OperadorDemo, OperadorEste];

    [Theory]
    [MemberData(nameof(Operadores))]
    public async Task Al_iniciar_la_api_queda_cargado_cada_operador_activo(Guid operador)
    {
        factory.CreateClient(); // arranca la API: migraciones y datos iniciales

        Assert.True(await ConsultarAsync<bool>(
            """SELECT "Activo" FROM administracion."Operadores" WHERE "Id" = @operador""", operador));
    }

    [Theory]
    [MemberData(nameof(Operadores))]
    public async Task Cada_operador_queda_con_una_relacion_activa_con_el_comercio_demo(Guid operador)
    {
        factory.CreateClient();

        Assert.Equal("Activa", await ConsultarAsync<string>(
            """SELECT "Estado" FROM administracion."RelacionesComerciales" WHERE "OperadorId" = @operador AND "ComercioId" = @comercio""",
            operador));
    }

    [Fact]
    public async Task Los_dos_operadores_tienen_un_maximo_de_paradas_por_ruta_distinto()
    {
        factory.CreateClient();
        const string sql = """SELECT "MaxParadasPorRuta" FROM administracion."VersionesReglasPlanificacion" WHERE "OperadorId" = @operador""";

        Assert.Equal(20, await ConsultarAsync<int>(sql, OperadorDemo));
        Assert.Equal(8, await ConsultarAsync<int>(sql, OperadorEste));
    }

    [Fact]
    public async Task Cada_operador_ve_solo_sus_zonas()
    {
        factory.CreateClient();
        using var scope = factory.Services.CreateScope();

        // Contextos distintos, uno por operador, como en AislamientoTests: lo que ve cada uno lo decide el filtro "Tenant".
        await using var comoDemo = Contexto(scope, OperadorDemo);
        await using var comoEste = Contexto(scope, OperadorEste);

        Assert.Equal(["MVD-DEMO"], await comoDemo.Zonas.Select(z => z.Codigo).ToListAsync());
        Assert.Equal(["CDC-ESTE"], await comoEste.Zonas.Select(z => z.Codigo).ToListAsync());
    }

    // CU-03: antes de elegir operador, la sesión del comercio no tiene inquilino; la consulta autorizada a ignorar el
    // filtro (ADR-0002, sección 2.4) igual le muestra los dos operadores con los que trabaja.
    [Fact]
    public async Task Tienda_Demo_puede_elegir_entre_los_dos_operadores_aunque_todavia_no_tenga_operador()
    {
        factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        await using var sinOperador = ActivatorUtilities.CreateInstance<AdministracionDbContext>(
            scope.ServiceProvider, new InquilinoFijo(null, ComercioDemo));

        var operadores = await new OperadoresDelComercio(sinOperador).ActivosAsync(ComercioDemo, CancellationToken.None);

        Assert.Equal([OperadorEste, OperadorDemo], operadores.Select(o => o.Id));
    }

    [Fact]
    public async Task Volver_a_ejecutar_los_datos_iniciales_no_duplica_nada()
    {
        factory.CreateClient();

        await factory.Services.SembrarDatosInicialesAsync();

        Assert.Equal(1L, await ConsultarAsync<long>("""SELECT count(*) FROM administracion."Comercios" WHERE "Id" = @comercio"""));

        foreach (var operador in new[] { OperadorDemo, OperadorEste })
        {
            Assert.Equal(1L, await ConsultarAsync<long>(
                """SELECT count(*) FROM administracion."Operadores" WHERE "Id" = @operador""", operador));
            Assert.Equal(1L, await ConsultarAsync<long>(
                """SELECT count(*) FROM administracion."RelacionesComerciales" WHERE "OperadorId" = @operador AND "ComercioId" = @comercio""",
                operador));
            Assert.Equal(2L, await ConsultarAsync<long>(
                """SELECT count(*) FROM administracion."Repartidores" WHERE "OperadorId" = @operador""", operador));
            Assert.Equal(1L, await ConsultarAsync<long>(
                """SELECT count(*) FROM administracion."VersionesReglasPlanificacion" WHERE "OperadorId" = @operador""", operador));
        }
    }

    // Un AdministracionDbContext real con el inquilino que la prueba quiere probar.
    private static AdministracionDbContext Contexto(IServiceScope scope, Guid operador)
    {
        return ActivatorUtilities.CreateInstance<AdministracionDbContext>(scope.ServiceProvider, new InquilinoFijo(operador, null));
    }

    // Lee directo de la base, sin pasar por la API ni por el filtro, para comprobar lo que quedó guardado.
    private async Task<T> ConsultarAsync<T>(string sql, Guid? operador = null)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("operador", operador ?? OperadorDemo);
        comando.Parameters.AddWithValue("comercio", ComercioDemo);

        return (T)(await comando.ExecuteScalarAsync())!;
    }
}

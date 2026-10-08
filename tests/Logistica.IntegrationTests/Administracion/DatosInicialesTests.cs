using Logistica.Modules.Administracion;
using Npgsql;

namespace Logistica.IntegrationTests.Administracion;

// Datos iniciales (ADR-0002, sección 2.8): al iniciar, la API deja cargados el operador y el comercio
// del inquilino provisorio, y volver a ejecutarlos no duplica nada.
public class DatosInicialesTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    // Los de appsettings.json de la API: TenantProvisorio y DatosIniciales tienen que coincidir.
    private static readonly Guid OperadorProvisorio = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ComercioProvisorio = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Al_iniciar_la_api_queda_cargado_el_operador_provisorio_activo()
    {
        factory.CreateClient(); // arranca la API: migraciones y datos iniciales

        Assert.True(await ConsultarAsync<bool>(
            """SELECT "Activo" FROM administracion."Operadores" WHERE "Id" = @operador"""));
    }

    [Fact]
    public async Task Al_iniciar_la_api_queda_activa_la_relacion_entre_el_operador_y_el_comercio_provisorios()
    {
        factory.CreateClient();

        Assert.Equal("Activa", await ConsultarAsync<string>(
            """SELECT "Estado" FROM administracion."RelacionesComerciales" WHERE "OperadorId" = @operador AND "ComercioId" = @comercio"""));
    }

    [Fact]
    public async Task Volver_a_ejecutar_los_datos_iniciales_no_duplica_nada()
    {
        factory.CreateClient();

        await factory.Services.SembrarDatosInicialesAsync();

        Assert.Equal(1L, await ConsultarAsync<long>("""SELECT count(*) FROM administracion."Operadores" WHERE "Id" = @operador"""));
        Assert.Equal(1L, await ConsultarAsync<long>("""SELECT count(*) FROM administracion."Comercios" WHERE "Id" = @comercio"""));
        Assert.Equal(1L, await ConsultarAsync<long>(
            """SELECT count(*) FROM administracion."RelacionesComerciales" WHERE "OperadorId" = @operador AND "ComercioId" = @comercio"""));
    }

    // Lee directo de la base, sin pasar por la API, para comprobar lo que quedó guardado.
    private async Task<T> ConsultarAsync<T>(string sql)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("operador", OperadorProvisorio);
        comando.Parameters.AddWithValue("comercio", ComercioProvisorio);

        return (T)(await comando.ExecuteScalarAsync())!;
    }
}

using Npgsql;

namespace Logistica.IntegrationTests.Identidad;

// La cookie de sesión se cifra con las claves de Data Protection, que se guardan en PostgreSQL y no en el disco del
// contenedor: App Platform lo descarta en cada despliegue y todos perderían la sesión (ClavesProteccionDbContext).
public class ClavesProteccionTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task Al_iniciar_sesion_la_clave_que_cifra_la_cookie_queda_en_la_base()
    {
        using var client = factory.CreateClient(Sesion.Opciones);

        await Sesion.IngresarAsync(client, "admin@operador-demo.uy");

        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var consulta = new NpgsqlCommand("""SELECT count(*) FROM proteccion."DataProtectionKeys" """, conexion);
        Assert.True((long)(await consulta.ExecuteScalarAsync())! >= 1);
    }
}

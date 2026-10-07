using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Logistica.IntegrationTests.BuildingBlocks;

// La garantía del addendum 3 del ADR-0001: cuando un caso de uso escribe en dos módulos, se guardan
// los dos cambios o ninguno. Dos instancias de DbContext hacen de "dos módulos" sobre la misma unidad
// de trabajo, contra un PostgreSQL real.
public sealed class UnidadDeTrabajoTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.6").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var unidad = new UnidadDeTrabajo(_postgres.GetConnectionString());
        await using var db = Crear(unidad);
        await db.Database.EnsureCreatedAsync();
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        return _postgres.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task Si_el_trabajo_termina_se_guardan_los_cambios_de_los_dos_modulos()
    {
        var textos = new[] { $"A-{Guid.NewGuid()}", $"B-{Guid.NewGuid()}" };
        await using var unidad = new UnidadDeTrabajo(_postgres.GetConnectionString());
        await using var moduloA = Crear(unidad);
        await using var moduloB = Crear(unidad);

        await unidad.EjecutarAsync(async () =>
        {
            moduloA.Notas.Add(new Nota(textos[0]));
            await moduloA.SaveChangesAsync();
            moduloB.Notas.Add(new Nota(textos[1]));
            await moduloB.SaveChangesAsync();
        }, CancellationToken.None);

        Assert.Equal(2L, await ContarAsync(textos));
    }

    [Fact]
    public async Task Si_el_trabajo_falla_no_queda_guardado_nada_de_ningun_modulo()
    {
        var textos = new[] { $"A-{Guid.NewGuid()}", $"B-{Guid.NewGuid()}" };
        await using var unidad = new UnidadDeTrabajo(_postgres.GetConnectionString());
        await using var moduloA = Crear(unidad);
        await using var moduloB = Crear(unidad);

        await Assert.ThrowsAsync<DomainException>(() => unidad.EjecutarAsync(async () =>
        {
            moduloA.Notas.Add(new Nota(textos[0]));
            await moduloA.SaveChangesAsync();
            moduloB.Notas.Add(new Nota(textos[1]));
            await moduloB.SaveChangesAsync();

            // Como si Envíos rechazara la transición después de que Depósito ya guardó la recepción.
            throw new DomainException("Falla después de guardar en los dos módulos.");
        }, CancellationToken.None));

        Assert.Equal(0L, await ContarAsync(textos));
    }

    [Fact]
    public async Task Fuera_de_una_unidad_de_trabajo_cada_DbContext_guarda_con_su_propia_transaccion()
    {
        var texto = $"C-{Guid.NewGuid()}";
        await using var unidad = new UnidadDeTrabajo(_postgres.GetConnectionString());
        await using var db = Crear(unidad);

        db.Notas.Add(new Nota(texto));
        await db.SaveChangesAsync();

        Assert.Equal(1L, await ContarAsync([texto]));
    }

    private static PruebaDbContext Crear(UnidadDeTrabajo unidad)
    {
        var options = new DbContextOptionsBuilder<PruebaDbContext>().UseNpgsql(unidad.Conexion).Options;
        return new PruebaDbContext(options, unidad);
    }

    // Lee con una conexión aparte: sólo ve lo que quedó confirmado.
    private async Task<long> ContarAsync(string[] textos)
    {
        await using var conexion = new NpgsqlConnection(_postgres.GetConnectionString());
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""SELECT count(*) FROM prueba."Notas" WHERE "Texto" = ANY(@textos)""", conexion);
        comando.Parameters.AddWithValue("textos", textos);

        return (long)(await comando.ExecuteScalarAsync())!;
    }

    public sealed class Nota(string texto) : Entity
    {
        public string Texto { get; private set; } = texto;
    }

    public sealed class PruebaDbContext(DbContextOptions<PruebaDbContext> options, UnidadDeTrabajo unidad)
        : ModuleDbContext(options, "prueba", unidad)
    {
        public DbSet<Nota> Notas => Set<Nota>();
    }
}

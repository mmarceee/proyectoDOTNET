using System.Text.Json;
using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.BuildingBlocks;

// La tabla outbox de cada módulo (ADR-0003, secciones 2.1 y 2.6). No lleva filtro "Tenant" (ADR-0002, sección 2.4):
// el publicador la lee sin inquilino. El inquilino viaja como metadato del sobre, tomado de la sesión que generó el evento.
public sealed class OutboxTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task El_publicador_lee_sin_inquilino_los_pendientes_de_todos_los_operadores_con_sus_metadatos()
    {
        using var scope = factory.Services.CreateScope();
        var (a, comercioDeA, b) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var tipo = TipoUnico();
        var ocurridoEn = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

        // Un evento generado por un usuario del comercio de A y otro por el personal de B.
        await RegistrarAsync(scope, new InquilinoFijo(a, comercioDeA), tipo, valor: 1, ocurridoEn);
        await RegistrarAsync(scope, new InquilinoFijo(b, null), tipo, valor: 2, ocurridoEn);

        // Como el publicador: sin inquilino, ve los pendientes de los dos operadores.
        await using var publicador = Contexto<EnviosDbContext>(scope, new InquilinoFijo(null, null));
        var pendientes = await publicador.Outbox
            .Where(m => m.Tipo == tipo && m.PublicadoEn == null)
            .OrderBy(m => m.OperadorId == a ? 0 : 1)
            .ToListAsync();

        Assert.Collection(pendientes,
            m =>
            {
                Assert.Equal((a, comercioDeA), (m.OperadorId, m.ComercioId));
                Assert.Equal(1, JsonDocument.Parse(m.Contenido).RootElement.GetProperty("Valor").GetInt32());
                Assert.Equal(ocurridoEn, m.OcurridoEn);
                Assert.NotEqual(Guid.Empty, m.Id);
            },
            m =>
            {
                Assert.Equal((b, (Guid?)null), (m.OperadorId, m.ComercioId));
                Assert.Equal(2, JsonDocument.Parse(m.Contenido).RootElement.GetProperty("Valor").GetInt32());
            });
    }

    [Fact]
    public void Sin_operador_en_la_sesion_no_se_puede_registrar_un_evento()
    {
        using var scope = factory.Services.CreateScope();
        using var sinOperador = Contexto<EnviosDbContext>(scope, new InquilinoFijo(null, Guid.NewGuid()));

        Assert.Throws<TenantMismatchException>(
            () => sinOperador.AgregarAlOutbox(TipoUnico(), new { Valor = 1 }, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task El_evento_se_revierte_si_falla_el_cambio_que_lo_origina()
    {
        using var scope = factory.Services.CreateScope();
        var tipo = TipoUnico();

        // El mismo SaveChanges guarda un envío de otro operador (lo rechaza el interceptor) y el evento.
        await using (var comoA = Contexto<EnviosDbContext>(scope, new InquilinoFijo(Guid.NewGuid(), null)))
        {
            comoA.Envios.Add(Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), $"ENV-{Guid.NewGuid():N}",
                new Destinatario("Ana", "099123456"), new Direccion("Calle", "1", "Montevideo", "Montevideo", "11300"),
                [new DatosBulto(1, 10, 10, 10, 100)], OrigenEvento.PortalComercio, null, DateTimeOffset.UtcNow));
            comoA.AgregarAlOutbox(tipo, new { Valor = 1 }, DateTimeOffset.UtcNow);

            await Assert.ThrowsAsync<TenantMismatchException>(() => comoA.SaveChangesAsync());
        }

        await using var publicador = Contexto<EnviosDbContext>(scope, new InquilinoFijo(null, null));
        Assert.False(await publicador.Outbox.AnyAsync(m => m.Tipo == tipo));
    }

    [Fact]
    public void Cada_modulo_tiene_su_tabla_outbox_en_su_schema_y_sin_filtro_Tenant()
    {
        using var scope = factory.Services.CreateScope();

        var outboxes = scope.ServiceProvider.GetServices<ModuleDbContext>()
            .Select(db => db.Model.FindEntityType(typeof(OutboxMessage)))
            .ToList();

        Assert.NotEmpty(outboxes);
        Assert.All(outboxes, t =>
        {
            Assert.NotNull(t);
            Assert.Equal("outbox_messages", t.GetTableName());
            Assert.Null(t.FindDeclaredQueryFilter(ModuleDbContext.FiltroTenant));
        });

        // Una tabla por módulo: cada una en un schema distinto.
        var schemas = outboxes.Select(t => t!.GetSchema()).ToList();
        Assert.Equal(schemas.Count, schemas.Distinct().Count());
    }

    private static T Contexto<T>(IServiceScope scope, ICurrentTenant tenant)
        where T : ModuleDbContext
    {
        return ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider, tenant);
    }

    private static async Task RegistrarAsync(
        IServiceScope scope, ICurrentTenant tenant, string tipo, int valor, DateTimeOffset ocurridoEn)
    {
        await using var db = Contexto<EnviosDbContext>(scope, tenant);
        db.AgregarAlOutbox(tipo, new { Valor = valor }, ocurridoEn);
        await db.SaveChangesAsync();
    }

    // Cada prueba usa su propio tipo de evento, para no ver los mensajes de las otras.
    private static string TipoUnico()
    {
        return $"Prueba{Guid.NewGuid():N}.v1";
    }
}

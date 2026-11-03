using System.Net;
using System.Net.Http.Json;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Repartidor;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Logistica.Modules.Ejecucion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Ejecucion;

// RF 32 de punta a punta (ADR-0004, sección 2.11 y pruebas 10 a 13): la visita de un envío con el resultado de cada bulto,
// sincronizada por la sesión real del repartidor. Se aplica entera o se rechaza entera, con el diagnóstico de cada bulto.
public sealed class VisitaPorBultoTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Url = "/api/repartidor/sincronizacion";
    private static readonly Guid Operador = DatosIniciales.OperadorDemoId;
    private static readonly string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jD8kAAAAASUVORK5CYII=";
    private static int _dias;

    [Fact]
    public async Task Una_visita_que_entrega_todos_los_bultos_deja_el_envio_entregado_con_la_firma_de_cada_uno()
    {
        var (rutaId, envios) = await RutaDespachadaAsync(bultosPorEnvio: 3);
        var codigos = await CodigosAsync(envios[0]);
        using var client = await RepartidorAsync();

        var resultado = await SincronizarAsync(client, Visita(rutaId, envios[0], 1, codigos.Select(c => new ResultadoBultoRequest(c))));

        Assert.Equal("Aplicada", resultado.Resultado);
        await using var db = Contexto<EnviosDbContext>();
        var envio = await db.Envios.Include(e => e.Intentos).SingleAsync(e => e.Id == envios[0]);
        Assert.Equal(EstadoEnvio.Entregado, envio.Estado);
        Assert.All(envio.Bultos, b => Assert.Equal(EstadoEnvio.Entregado, b.Estado));
        Assert.Equal((3, true), (envio.BultosEntregados, envio.Terminado));
        Assert.Equal(envio.Bultos.Select(b => b.Id).Order(), Assert.Single(envio.Intentos).BultoIds.Order());
        Assert.True(await db.Outbox.AnyAsync(m => m.Tipo == "EnvioEntregado.v1" && m.Contenido.Contains(envios[0].ToString())));
    }

    // Prueba 10 del ADR-0004: bultos realmente entregados que no se aplican, porque otro de la visita está en conflicto.
    [Fact]
    public async Task Un_bulto_extraviado_rechaza_la_visita_entera_y_otro_envio_de_la_parada_se_aplica()
    {
        var (rutaId, envios) = await RutaDespachadaAsync(bultosPorEnvio: 3, cantidad: 2);
        var codigos = await CodigosAsync(envios[0]);
        var extraviado = await ExtraviarBultoAsync(envios[0], codigos[1]);
        using var client = await RepartidorAsync();

        var respuesta = await client.PostAsJsonAsync(Url, new SincronizacionRequest(
        [
            Visita(rutaId, envios[0], 1, codigos.Select(c => new ResultadoBultoRequest(c))),
            Visita(rutaId, envios[1], 2, (await CodigosAsync(envios[1])).Select(c => new ResultadoBultoRequest(c))),
        ]));
        var resultados = (await respuesta.Content.ReadFromJsonAsync<SincronizacionResponse>())!.Resultados;

        Assert.Equal(("Conflicto", "BultoExtraviado"), (resultados[0].Resultado, resultados[0].Motivo));
        Assert.Equal([new BultoRechazadoResponse(extraviado, "BultoExtraviado")], resultados[0].Bultos);
        Assert.Equal("Aplicada", resultados[1].Resultado);

        await using var db = Contexto<EnviosDbContext>();
        var envio = await db.Envios.Include(e => e.Intentos).SingleAsync(e => e.Id == envios[0]);
        Assert.Equal(EstadoEnvio.EnTransito, envio.Estado);
        Assert.Equal(2, envio.Bultos.Count(b => b.Estado == EstadoEnvio.EnTransito));
        Assert.Equal(0, envio.BultosEntregados);
        Assert.Empty(envio.Intentos);
        Assert.False(await db.Outbox.AnyAsync(m => m.Tipo == "EnvioEntregado.v1" && m.Contenido.Contains(envios[0].ToString())));

        // La visita queda guardada con su firma y el diagnóstico, pendiente de revisión (sección 2.5).
        await using var ejecucion = Contexto<EjecucionDbContext>();
        var guardada = await ejecucion.Operaciones.SingleAsync(o => o.EnvioId == envios[0]);
        Assert.Equal((ResultadoOperacion.Conflicto, false), (guardada.Resultado, guardada.Revisada));
        var operacion = System.Text.Json.JsonSerializer.Deserialize<OperacionDispositivoRequest>(guardada.Contenido!);
        Assert.Equal(Png, operacion?.Visita?.Firma?.ContenidoBase64);
        Assert.Contains(extraviado, guardada.Bultos!);
        Assert.Equal($"{extraviado}: BultoExtraviado", guardada.Detalle);
    }

    // Prueba 11 del ADR-0004 (la versión de reglas de los datos iniciales no admite entrega parcial).
    [Fact]
    public async Task Sin_entrega_parcial_una_visita_que_entrega_parte_de_los_bultos_es_invalida()
    {
        var (rutaId, envios) = await RutaDespachadaAsync(bultosPorEnvio: 2);
        var codigos = await CodigosAsync(envios[0]);
        using var client = await RepartidorAsync();

        var resultado = await SincronizarAsync(client, Visita(rutaId, envios[0], 1,
            [new ResultadoBultoRequest(codigos[0]), new ResultadoBultoRequest(codigos[1], await MotivoAsync("AUSENTE"))]));

        Assert.Equal(("Invalida", "EntregaParcialNoAdmitida"), (resultado.Resultado, resultado.Motivo));
        Assert.Equal(EstadoEnvio.EnTransito, await EstadoAsync(envios[0]));
    }

    // Prueba 12 del ADR-0004.
    [Fact]
    public async Task Una_visita_con_un_bulto_ajeno_repetido_o_sin_resultado_es_invalida_con_el_motivo_de_cada_uno()
    {
        var (rutaId, envios) = await RutaDespachadaAsync(bultosPorEnvio: 3);
        var codigos = await CodigosAsync(envios[0]);
        using var client = await RepartidorAsync();

        var resultado = await SincronizarAsync(client, Visita(rutaId, envios[0], 1,
            [new(codigos[0]), new(codigos[0]), new("NO-ES-DEL-ENVIO")]));

        Assert.Equal(("Invalida", "BultoRepetido"), (resultado.Resultado, resultado.Motivo));
        Assert.Equal(
            [
                new BultoRechazadoResponse(codigos[0], "BultoRepetido"),
                new BultoRechazadoResponse("NO-ES-DEL-ENVIO", "BultoAjenoAlEnvio"),
                new BultoRechazadoResponse(codigos[1], "BultoSinResultado"),
                new BultoRechazadoResponse(codigos[2], "BultoSinResultado"),
            ],
            resultado.Bultos);
        Assert.Equal(EstadoEnvio.EnTransito, await EstadoAsync(envios[0]));
    }

    // Prueba 13 del ADR-0004: un reenvío responde lo mismo y una visita rechazada no consume intentos.
    [Fact]
    public async Task Reenviar_una_visita_rechazada_responde_el_mismo_diagnostico_sin_consumir_intentos()
    {
        var (rutaId, envios) = await RutaDespachadaAsync(bultosPorEnvio: 2);
        var codigos = await CodigosAsync(envios[0]);
        var extraviado = await ExtraviarBultoAsync(envios[0], codigos[0]);
        using var client = await RepartidorAsync();
        var motivo = await MotivoAsync("AUSENTE");
        var visita = Visita(rutaId, envios[0], 1, codigos.Select(c => new ResultadoBultoRequest(c, motivo)));

        var primera = await SincronizarAsync(client, visita);
        var reenvio = await SincronizarAsync(client, visita);

        Assert.Equal(("Conflicto", "BultoExtraviado"), (primera.Resultado, primera.Motivo));
        Assert.Equal((primera.Resultado, primera.Motivo, primera.Detalle), (reenvio.Resultado, reenvio.Motivo, reenvio.Detalle));
        Assert.Equal(primera.Bultos, reenvio.Bultos);
        Assert.Equal([new BultoRechazadoResponse(extraviado, "BultoExtraviado")], reenvio.Bultos);

        await using var db = Contexto<EnviosDbContext>();
        var envio = await db.Envios.Include(e => e.Intentos).SingleAsync(e => e.Id == envios[0]);
        Assert.Empty(envio.Intentos);
        Assert.All(envio.Bultos, b => Assert.Equal(0, b.IntentosFallidos));
    }

    // ADR-0004, sección 2.11: una entrega anterior a la entrega parcial vale para todos los bultos del envío.
    [Fact]
    public async Task Una_entrega_de_la_PWA_anterior_entrega_todos_los_bultos_del_envio()
    {
        var (rutaId, envios) = await RutaDespachadaAsync(bultosPorEnvio: 2);
        using var client = await RepartidorAsync();

        var resultado = await SincronizarAsync(client,
            new OperacionDispositivoRequest(Guid.CreateVersion7(), Guid.NewGuid(), 1, "RegistrarEntrega", rutaId, envios[0],
                DateTimeOffset.UtcNow.AddMinutes(-10), -34.9m, -56.16m,
                Entrega: new EntregaRequest(new ArchivoRequest("image/png", Png))));

        Assert.Equal("Aplicada", resultado.Resultado);
        await using var db = Contexto<EnviosDbContext>();
        var envio = await db.Envios.SingleAsync(e => e.Id == envios[0]);
        Assert.All(envio.Bultos, b => Assert.Equal(EstadoEnvio.Entregado, b.Estado));
        Assert.Equal(2, envio.BultosEntregados);
    }

    // Una operación vieja sobre un envío con un bulto extraviado conserva el motivo por envío que ya conoce la PWA.
    [Fact]
    public async Task Una_entrega_de_la_PWA_anterior_sobre_un_bulto_extraviado_es_un_conflicto_con_el_motivo_de_siempre()
    {
        var (rutaId, envios) = await RutaDespachadaAsync(bultosPorEnvio: 2);
        await ExtraviarBultoAsync(envios[0], (await CodigosAsync(envios[0]))[1]);
        using var client = await RepartidorAsync();

        var resultado = await SincronizarAsync(client,
            new OperacionDispositivoRequest(Guid.CreateVersion7(), Guid.NewGuid(), 1, "RegistrarEntrega", rutaId, envios[0],
                DateTimeOffset.UtcNow.AddMinutes(-10), -34.9m, -56.16m,
                Entrega: new EntregaRequest(new ArchivoRequest("image/png", Png))));

        Assert.Equal(("Conflicto", "EnvioExtraviado"), (resultado.Resultado, resultado.Motivo));
        Assert.Null(resultado.Bultos);
        Assert.Equal(EstadoEnvio.EnTransito, await EstadoAsync(envios[0]));
    }

    // ---- Ayudas ----

    private async Task<HttpClient> RepartidorAsync()
    {
        var client = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
            .CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        return client;
    }

    private static OperacionDispositivoRequest Visita(Guid rutaId, Guid envioId, long secuencia,
        IEnumerable<ResultadoBultoRequest> bultos) =>
        new(Guid.CreateVersion7(), Guid.NewGuid(), secuencia, "RegistrarVisita", rutaId, envioId,
            DateTimeOffset.UtcNow.AddMinutes(-10), -34.9m, -56.16m,
            Visita: new VisitaRequest(bultos.ToList(), new ArchivoRequest("image/png", Png)));

    private static async Task<ResultadoOperacionResponse> SincronizarAsync(HttpClient client, OperacionDispositivoRequest operacion)
    {
        var respuesta = await client.PostAsJsonAsync(Url, new SincronizacionRequest([operacion]));
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return Assert.Single((await respuesta.Content.ReadFromJsonAsync<SincronizacionResponse>())!.Resultados);
    }

    // Envíos EnTransito, con todos sus bultos, en una ruta despachada del repartidor de demostración. Cada ruta en un día
    // distinto: un repartidor no puede tener dos rutas activas el mismo día.
    private async Task<(Guid RutaId, Guid[] Envios)> RutaDespachadaAsync(int bultosPorEnvio, int cantidad = 1)
    {
        var ahora = DateTimeOffset.UtcNow.AddHours(-2);
        var envios = Enumerable.Range(0, cantidad).Select(_ =>
        {
            var envio = Envio.Crear(Operador, DatosIniciales.ComercioDemoId, $"ENV-{Guid.NewGuid():N}",
                new Destinatario("Ana", "099123456"), new Direccion("Calle", "1", "Montevideo", "Montevideo", "11300"),
                Enumerable.Repeat(new DatosBulto(1, 10, 10, 10, 100), bultosPorEnvio).ToList(), OrigenEvento.PortalComercio,
                null, ahora);
            envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora);
            envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora);
            envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, ahora);
            return envio;
        }).ToList();

        await using (var db = Contexto<EnviosDbContext>())
        {
            db.Envios.AddRange(envios);
            await db.SaveChangesAsync();
        }

        var fecha = new DateOnly(2041, 1, 1).AddDays(Interlocked.Increment(ref _dias));
        var ruta = Ruta.Crear(Operador, fecha, DatosIniciales.RepartidorDemoId, Guid.NewGuid(),
            envios.Select(e => new EnvioRuta(e.Id, e.Numero, null, null, null, false, [])).ToList(), ahora);
        ruta.Despachar(ahora);

        await using (var db = Contexto<PlanificacionDbContext>())
        {
            db.Rutas.Add(ruta);
            await db.SaveChangesAsync();
        }

        return (ruta.Id, envios.Select(e => e.Id).ToArray());
    }

    private async Task<string[]> CodigosAsync(Guid envioId)
    {
        await using var db = Contexto<EnviosDbContext>();
        return (await db.Envios.AsNoTracking().SingleAsync(e => e.Id == envioId)).Bultos
            .Select(b => b.Codigo).Order().ToArray();
    }

    // Un bulto declarado extraviado mientras el repartidor estaba sin conexión (T16 de ese bulto).
    private async Task<string> ExtraviarBultoAsync(Guid envioId, string codigo)
    {
        await using var db = Contexto<EnviosDbContext>();
        var envio = await db.Envios.SingleAsync(e => e.Id == envioId);
        envio.Bultos.Single(b => b.Codigo == codigo).Transicionar(EstadoEnvio.Extraviado);
        await db.SaveChangesAsync();
        return codigo;
    }

    private async Task<EstadoEnvio> EstadoAsync(Guid envioId)
    {
        await using var db = Contexto<EnviosDbContext>();
        return (await db.Envios.AsNoTracking().SingleAsync(e => e.Id == envioId)).Estado;
    }

    // Un motivo del catálogo de la versión de reglas vigente del operador (datos iniciales).
    private async Task<Guid> MotivoAsync(string codigo)
    {
        await using var db = Contexto<AdministracionDbContext>();
        var ahora = DateTimeOffset.UtcNow;
        var vigente = await db.VersionesReglas
            .Where(v => v.VigenteDesde <= ahora && (v.VigenteHasta == null || v.VigenteHasta > ahora))
            .OrderByDescending(v => v.VigenteDesde).Select(v => v.Id).FirstAsync();
        return await db.MotivosNoEntrega.Where(m => m.VersionReglasId == vigente && m.Codigo == codigo)
            .Select(m => m.Id).SingleAsync();
    }

    // Un DbContext del operador de demostración, como personal del operador.
    private T Contexto<T>() where T : Logistica.BuildingBlocks.Infrastructure.Persistence.ModuleDbContext
    {
        var scope = factory.Services.CreateScope();
        return ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider, new InquilinoFijo(Operador, null));
    }
}

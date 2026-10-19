using System.Net;
using System.Net.Http.Json;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Repartidor;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Ejecucion.Domain.Operaciones;
using Logistica.Modules.Ejecucion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Ejecucion;

// CU-52 de punta a punta, con la sesión real del repartidor y sin el respaldo provisorio: HTTP, Ejecución, Planificación,
// Envíos y PostgreSQL. Verifica la política del ADR-0004: idempotencia, conflictos, operaciones inválidas, pertenencia,
// dependencias dentro del lote y la hora del dispositivo.
public sealed class SincronizacionTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Url = "/api/repartidor/sincronizacion";
    private static readonly Guid Operador = DatosIniciales.OperadorDemoId;
    private static readonly string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jD8kAAAAASUVORK5CYII=";
    private static int _dias;

    private HttpClient Cliente() => factory
        .WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
        .CreateClient(Sesion.Opciones);

    private async Task<HttpClient> RepartidorAsync()
    {
        var client = Cliente();
        await Sesion.IngresarAsync(client, "repartidor@operador-demo.uy");
        return client;
    }

    [Fact]
    public async Task Una_entrega_se_aplica_una_sola_vez_aunque_la_PWA_reenvie_el_lote()
    {
        var (rutaId, envios) = await RutaDespachadaAsync(1);
        using var client = await RepartidorAsync();
        var entrega = Entrega(rutaId, envios[0], secuencia: 1);

        var primera = await SincronizarAsync(client, entrega);
        var reenvio = await SincronizarAsync(client, entrega);

        Assert.Equal("Aplicada", primera.Resultado);
        Assert.Equal("YaAplicada", reenvio.Resultado);

        await using var db = Contexto<EnviosDbContext>();
        var envio = await db.Envios.Include(e => e.Intentos).SingleAsync(e => e.Id == envios[0]);
        Assert.Equal(EstadoEnvio.Entregado, envio.Estado);
        Assert.Single(envio.Intentos);
        Assert.True(await db.ArchivosEvidencia.AnyAsync(a => a.EnvioId == envios[0]));
        Assert.True(await db.Outbox.AnyAsync(m => m.Tipo == "EnvioEntregado.v1" && m.Contenido.Contains(envios[0].ToString())));
    }

    [Fact]
    public async Task Una_entrega_de_un_envio_extraviado_es_un_conflicto_y_se_guarda_con_su_firma_sin_aplicarse()
    {
        var (rutaId, envios) = await RutaDespachadaAsync(1);
        await CambiarEstadoAsync(envios[0], EstadoEnvio.Extraviado); // robo durante el reparto (T16)
        using var client = await RepartidorAsync();
        var entrega = Entrega(rutaId, envios[0], secuencia: 1);

        var resultado = await SincronizarAsync(client, entrega);

        Assert.Equal(("Conflicto", "EnvioExtraviado"), (resultado.Resultado, resultado.Motivo));
        Assert.Equal(EstadoEnvio.Extraviado, await EstadoAsync(envios[0]));

        await using var ejecucion = Contexto<EjecucionDbContext>();
        var registrada = await ejecucion.Operaciones.SingleAsync(o => o.Id == entrega.OperacionId);
        Assert.Equal(ResultadoOperacion.Conflicto, registrada.Resultado);
        Assert.False(registrada.Revisada);
        // La operación se conserva completa, con su firma (ADR-0004, sección 2.5).
        var guardada = System.Text.Json.JsonSerializer.Deserialize<OperacionDispositivoRequest>(registrada.Contenido!);
        Assert.Equal(Png, guardada?.Entrega?.Firma.ContenidoBase64);
    }

    [Fact]
    public async Task Una_entrega_con_una_firma_que_no_es_una_imagen_es_invalida_y_el_envio_no_cambia()
    {
        var (rutaId, envios) = await RutaDespachadaAsync(1);
        using var client = await RepartidorAsync();
        var entrega = Entrega(rutaId, envios[0], secuencia: 1) with
        {
            Entrega = new EntregaRequest(new ArchivoRequest("text/plain", Png)),
        };

        var resultado = await SincronizarAsync(client, entrega);

        Assert.Equal(("Invalida", "PruebaInvalida"), (resultado.Resultado, resultado.Motivo));
        Assert.Equal(EstadoEnvio.EnTransito, await EstadoAsync(envios[0]));
    }

    [Fact]
    public async Task Una_operacion_sobre_la_ruta_de_otro_repartidor_es_un_conflicto()
    {
        var (rutaId, envios) = await RutaDespachadaAsync(1, repartidorId: Guid.Parse("42222222-2222-2222-2222-222222222222"));
        using var client = await RepartidorAsync();

        var resultado = await SincronizarAsync(client, Entrega(rutaId, envios[0], secuencia: 1));

        Assert.Equal(("Conflicto", "RutaNoAsignada"), (resultado.Resultado, resultado.Motivo));
        Assert.Equal(EstadoEnvio.EnTransito, await EstadoAsync(envios[0]));
    }

    [Fact]
    public async Task Un_conflicto_rechaza_lo_que_sigue_del_mismo_envio_y_no_frena_a_los_demas()
    {
        var (rutaId, envios) = await RutaDespachadaAsync(2);
        await CambiarEstadoAsync(envios[0], EstadoEnvio.Extraviado);
        using var client = await RepartidorAsync();

        // Enviadas en desorden: el servidor las procesa por Secuencia.
        var respuesta = await client.PostAsJsonAsync(Url, new SincronizacionRequest(
        [
            Entrega(rutaId, envios[1], secuencia: 3),
            Intento(rutaId, envios[0], secuencia: 2, ReglasDeEntregaProvisorias.Ausente.Id),
            Entrega(rutaId, envios[0], secuencia: 1),
        ]));
        var resultados = (await respuesta.Content.ReadFromJsonAsync<SincronizacionResponse>())!.Resultados;

        Assert.Equal(new[] { "Conflicto", "Conflicto", "Aplicada" }, resultados.Select(r => r.Resultado));
        Assert.Equal(new[] { "EnvioExtraviado", "DependeDeOperacionRechazada", null }, resultados.Select(r => r.Motivo));
        Assert.Equal(EstadoEnvio.Entregado, await EstadoAsync(envios[1]));
    }

    [Fact]
    public async Task Un_intento_fallido_con_un_motivo_no_reprogramable_inicia_la_devolucion()
    {
        var (rutaId, envios) = await RutaDespachadaAsync(1);
        using var client = await RepartidorAsync();

        var resultado = await SincronizarAsync(client,
            Intento(rutaId, envios[0], secuencia: 1, ReglasDeEntregaProvisorias.Rechazado.Id));

        Assert.Equal("Aplicada", resultado.Resultado);
        Assert.Equal(EstadoEnvio.EnDevolucion, await EstadoAsync(envios[0]));
        await using var db = Contexto<EnviosDbContext>();
        Assert.True(await db.Devoluciones.AnyAsync(d => d.EnvioId == envios[0]));
    }

    // Letra, sección 6.12; ADR-0004, sección 2.9: la operación continúa la traza que empezó en el dispositivo, y su
    // evento sale por el outbox con esa misma traza, para seguirla hasta el Worker.
    [Fact]
    public async Task La_operacion_continua_la_traza_del_dispositivo_hasta_el_evento_del_outbox()
    {
        var (rutaId, envios) = await RutaDespachadaAsync(1);
        using var client = await RepartidorAsync();
        var trazaDelDispositivo = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray());
        var entrega = Entrega(rutaId, envios[0], secuencia: 1) with
        {
            CorrelacionId = $"00-{trazaDelDispositivo}-{Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()[..8])}-01",
        };

        Assert.Equal("Aplicada", (await SincronizarAsync(client, entrega)).Resultado);

        await using var db = Contexto<EnviosDbContext>();
        var evento = await db.Outbox.SingleAsync(m => m.Tipo == "EnvioEntregado.v1" && m.Contenido.Contains(envios[0].ToString()));
        Assert.StartsWith($"00-{trazaDelDispositivo}-", evento.TraceParent);
    }

    [Fact]
    public async Task Una_hora_del_dispositivo_adelantada_se_reemplaza_por_la_de_recepcion_y_queda_marcada()
    {
        var (rutaId, envios) = await RutaDespachadaAsync(1);
        using var client = await RepartidorAsync();
        var entrega = Entrega(rutaId, envios[0], secuencia: 1) with { OcurridoEn = DateTimeOffset.UtcNow.AddHours(3) };

        Assert.Equal("Aplicada", (await SincronizarAsync(client, entrega)).Resultado);

        await using var ejecucion = Contexto<EjecucionDbContext>();
        var registrada = await ejecucion.Operaciones.SingleAsync(o => o.Id == entrega.OperacionId);
        Assert.True(registrada.HoraCorregida);
        Assert.True(registrada.OcurridoEn < DateTimeOffset.UtcNow.AddMinutes(1));
    }

    // ADR-0004, sección 1.2: la rendición se hizo antes de que el celular sincronizara (T8), o el envío ya tiene resultado.
    [Theory]
    [InlineData("EnDeposito", "EnvioEnDeposito")]
    [InlineData("Entregado", "EnvioYaResuelto")]
    public async Task Una_entrega_de_un_envio_que_ya_no_esta_EnTransito_es_un_conflicto(string estado, string motivo)
    {
        var (rutaId, envios) = await RutaDespachadaAsync(1);
        using var client = await RepartidorAsync();
        if (estado == "Entregado")
        {
            Assert.Equal("Aplicada", (await SincronizarAsync(client, Entrega(rutaId, envios[0], secuencia: 1))).Resultado);
        }
        else
        {
            await CambiarEstadoAsync(envios[0], Enum.Parse<EstadoEnvio>(estado));
        }

        var resultado = await SincronizarAsync(client, Entrega(rutaId, envios[0], secuencia: 2));

        Assert.Equal(("Conflicto", motivo), (resultado.Resultado, resultado.Motivo));
    }

    // Lo que la PWA no debería mandar nunca: se rechaza como inválido, se guarda y no cambia el envío.
    [Theory]
    [InlineData("TipoDesconocido", "TipoNoSoportado")]
    [InlineData("SinPosicion", "DatosIncompletos")]
    [InlineData("SinDatosDeEntrega", "DatosIncompletos")]
    [InlineData("FirmaNoBase64", "ArchivoInvalido")]
    [InlineData("MotivoDesconocido", "PruebaInvalida")]
    public async Task Una_operacion_mal_formada_es_invalida_y_el_envio_no_cambia(string caso, string motivo)
    {
        var (rutaId, envios) = await RutaDespachadaAsync(1);
        using var client = await RepartidorAsync();
        var entrega = Entrega(rutaId, envios[0], secuencia: 1);
        var operacion = caso switch
        {
            "TipoDesconocido" => entrega with { Tipo = "TipoDesconocido" },
            "SinPosicion" => entrega with { Latitud = null },
            "SinDatosDeEntrega" => entrega with { Entrega = null },
            "FirmaNoBase64" => entrega with { Entrega = new EntregaRequest(new ArchivoRequest("image/png", "no es base64!")) },
            _ => Intento(rutaId, envios[0], secuencia: 1, Guid.NewGuid()),
        };

        var resultado = await SincronizarAsync(client, operacion);

        Assert.Equal(("Invalida", motivo), (resultado.Resultado, resultado.Motivo));
        Assert.Equal(EstadoEnvio.EnTransito, await EstadoAsync(envios[0]));
    }

    [Fact]
    public async Task Reenviar_una_operacion_rechazada_responde_el_mismo_rechazo()
    {
        var (rutaId, envios) = await RutaDespachadaAsync(1);
        await CambiarEstadoAsync(envios[0], EstadoEnvio.Extraviado);
        using var client = await RepartidorAsync();
        var entrega = Entrega(rutaId, envios[0], secuencia: 1);

        var primera = await SincronizarAsync(client, entrega);
        var reenvio = await SincronizarAsync(client, entrega);

        Assert.Equal(primera, reenvio);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Un_lote_vacio_o_de_mas_de_20_operaciones_se_rechaza_entero(int cantidad)
    {
        using var client = await RepartidorAsync();
        var operaciones = Enumerable.Range(1, cantidad).Select(i => Entrega(Guid.NewGuid(), Guid.NewGuid(), i)).ToList();

        var respuesta = await client.PostAsJsonAsync(Url, new SincronizacionRequest(operaciones));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("despacho@operador-demo.uy", HttpStatusCode.Forbidden)]
    public async Task Solo_el_repartidor_puede_sincronizar(string? email, HttpStatusCode esperado)
    {
        using var client = Cliente();
        if (email is not null)
        {
            await Sesion.IngresarAsync(client, email);
        }

        var respuesta = await client.PostAsJsonAsync(Url,
            new SincronizacionRequest([Entrega(Guid.NewGuid(), Guid.NewGuid(), secuencia: 1)]));

        Assert.Equal(esperado, respuesta.StatusCode);
    }

    // ---- Ayudas ----

    private static OperacionDispositivoRequest Entrega(Guid rutaId, Guid envioId, long secuencia) =>
        new(Guid.CreateVersion7(), Guid.NewGuid(), secuencia, "RegistrarEntrega", rutaId, envioId,
            DateTimeOffset.UtcNow.AddMinutes(-30).ToOffset(TimeSpan.FromHours(-3)), -34.9m, -56.16m,
            Entrega: new EntregaRequest(new ArchivoRequest("image/png", Png)));

    private static OperacionDispositivoRequest Intento(Guid rutaId, Guid envioId, long secuencia, Guid motivoId) =>
        new(Guid.CreateVersion7(), Guid.NewGuid(), secuencia, "RegistrarIntentoFallido", rutaId, envioId,
            DateTimeOffset.UtcNow.AddMinutes(-20), -34.9m, -56.16m,
            IntentoFallido: new IntentoFallidoRequest(motivoId));

    private static async Task<ResultadoOperacionResponse> SincronizarAsync(HttpClient client, OperacionDispositivoRequest operacion)
    {
        var respuesta = await client.PostAsJsonAsync(Url, new SincronizacionRequest([operacion]));
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return Assert.Single((await respuesta.Content.ReadFromJsonAsync<SincronizacionResponse>())!.Resultados);
    }

    // Envíos EnTransito en una ruta despachada del repartidor de demostración (o del indicado). Cada ruta en un día
    // distinto: un repartidor no puede tener dos rutas activas el mismo día.
    private async Task<(Guid RutaId, Guid[] Envios)> RutaDespachadaAsync(int cantidad, Guid? repartidorId = null)
    {
        var ahora = DateTimeOffset.UtcNow.AddHours(-2);
        var envios = Enumerable.Range(0, cantidad).Select(_ =>
        {
            var envio = Envio.Crear(Operador, DatosIniciales.ComercioDemoId, $"ENV-{Guid.NewGuid():N}",
                new Destinatario("Ana", "099123456"), new Direccion("Calle", "1", "Montevideo", "Montevideo", "11300"),
                [new DatosBulto(1, 10, 10, 10, 100)], OrigenEvento.PortalComercio, null, ahora);
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

        var fecha = new DateOnly(2040, 1, 1).AddDays(Interlocked.Increment(ref _dias));
        var ruta = Ruta.Crear(Operador, fecha, repartidorId ?? DatosIniciales.RepartidorDemoId, Guid.NewGuid(),
            envios.Select(e => new EnvioRuta(e.Id, e.Numero, null, null, null, false, [])).ToList(), ahora);
        ruta.Despachar(ahora);

        await using (var db = Contexto<PlanificacionDbContext>())
        {
            db.Rutas.Add(ruta);
            await db.SaveChangesAsync();
        }

        return (ruta.Id, envios.Select(e => e.Id).ToArray());
    }

    private async Task CambiarEstadoAsync(Guid envioId, EstadoEnvio estado)
    {
        await using var db = Contexto<EnviosDbContext>();
        var envio = await db.Envios.SingleAsync(e => e.Id == envioId);
        envio.Transicionar(estado, OrigenEvento.Backoffice, null, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private async Task<EstadoEnvio> EstadoAsync(Guid envioId)
    {
        await using var db = Contexto<EnviosDbContext>();
        return (await db.Envios.AsNoTracking().SingleAsync(e => e.Id == envioId)).Estado;
    }

    // Un DbContext del operador de demostración, como personal del operador.
    private T Contexto<T>() where T : Logistica.BuildingBlocks.Infrastructure.Persistence.ModuleDbContext
    {
        var scope = factory.Services.CreateScope();
        return ActivatorUtilities.CreateInstance<T>(scope.ServiceProvider, new InquilinoFijo(Operador, null));
    }
}

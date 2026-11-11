using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Ejecucion.Domain.Rendiciones;
using Logistica.Modules.Ejecucion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Application.Features.IniciarDevolucion;
using Logistica.Modules.Envios.Contracts.Events;
using Logistica.Modules.Envios.Domain.Devoluciones;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Domain.Incidencias;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Planificacion.Domain.Rutas;
using Logistica.Modules.Planificacion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Deposito;

// CU-31 de punta a punta, desde la página del operario de depósito: Depósito coordina y Envíos cierra la devolución,
// pasa el envío a Devuelto (T15) y publica EnvioDevuelto. La devolución queda lista para entregar cuando sus bultos
// vuelven en la rendición (CU-55).
public sealed class ConfirmarDevolucionTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Pagina = "/backoffice/devoluciones";
    private static int dia;

    [Fact]
    public async Task La_rendicion_deja_en_el_deposito_la_devolucion_cuyos_bultos_volvieron()
    {
        var envio = await EnDevolucionAsync(recibida: false);
        var ruta = await RendicionConDevolucionAsync(envio);
        using var operario = await OperarioAsync();

        var rendicion = $"/backoffice/rendiciones/{ruta}";
        var respuesta = await operario.PostAsync($"{rendicion}?handler=Confirmar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Escaneados"] = string.Join("\n", envio.Bultos.Select(b => b.Codigo)),
            ["__RequestVerificationToken"] = await Sesion.TokenAsync(operario, rendicion),
        }));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        var devolucion = await DevolucionAsync(envio.Id);
        Assert.Equal(EstadoDevolucion.RecibidaEnDeposito, devolucion.Estado);
        Assert.NotNull(devolucion.RecibidaEnDepositoEn);
        Assert.Contains("Confirmar la entrega", await operario.GetStringAsync($"{Pagina}?Buscar={envio.Numero}"));
    }

    [Fact]
    public async Task Entregar_registra_quien_la_recibio_cierra_la_devolucion_y_publica_EnvioDevuelto()
    {
        var envio = await EnDevolucionAsync();
        using var operario = await OperarioAsync();
        // Como lo escribe un lector: en minúsculas y con espacios.
        var busqueda = $"  {envio.Bultos[1].Codigo.ToLowerInvariant()} ";
        Assert.Contains(envio.Numero, await operario.GetStringAsync($"{Pagina}?Buscar={Uri.EscapeDataString(busqueda)}"));

        var respuesta = await ConfirmarAsync(operario, busqueda, envio.Bultos.Select(b => b.Id), "María Gómez", "1.234.567-8");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(Pagina, respuesta.Headers.Location?.OriginalString);
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var guardado = await db.Envios.AsNoTracking().Include(e => e.Eventos).SingleAsync(e => e.Id == envio.Id);
        Assert.Equal(EstadoEnvio.Devuelto, guardado.Estado);
        Assert.Contains(guardado.Eventos, e => e.EstadoNuevo == EstadoEnvio.Devuelto && e.Detalle!.Contains("María Gómez"));
        var devolucion = await DevolucionAsync(envio.Id);
        Assert.Equal((EstadoDevolucion.Cerrada, "María Gómez", "1.234.567-8"),
            (devolucion.Estado, devolucion.NombreReceptor!, devolucion.DocumentoReceptor!));
        Assert.NotNull(devolucion.EntregadaAlComercioEn);
        Assert.Single(await db.Outbox.Where(m => m.Tipo == EnvioDevuelto.Tipo && m.Contenido.Contains(envio.Id.ToString())).ToListAsync());
        Assert.Empty(await db.Incidencias.Where(i => i.EnvioId == envio.Id).ToListAsync());
        Assert.Contains("ya se entregó al comercio", await operario.GetStringAsync($"{Pagina}?Buscar={envio.Numero}"));
    }

    [Fact]
    public async Task A2_sin_todos_los_bultos_pide_confirmar_y_registra_la_incidencia_con_los_que_faltaron()
    {
        var envio = await EnDevolucionAsync();
        using var operario = await OperarioAsync();

        var primera = await ConfirmarAsync(operario, envio.Numero, [envio.Bultos[0].Id], "María Gómez", "12345678");
        var pagina = await SeguirAsync(operario, primera);
        Assert.Contains("Confirmar entrega parcial", pagina);
        // Vuelve con lo que cargó el operario: el receptor y el bulto que desmarcó.
        Assert.Contains("María Gómez", pagina);
        Assert.Matches($"value=\"{envio.Bultos[0].Id}\"[^>]*checked", pagina);
        Assert.DoesNotMatch($"value=\"{envio.Bultos[1].Id}\"[^>]*checked", pagina);
        Assert.Equal(EstadoEnvio.EnDevolucion, await EstadoAsync(envio.Id));

        var segunda = await ConfirmarAsync(operario, envio.Numero, [envio.Bultos[0].Id], "María Gómez", "12345678", parcial: true);

        Assert.Equal(HttpStatusCode.Redirect, segunda.StatusCode);
        Assert.Equal(EstadoEnvio.Devuelto, await EstadoAsync(envio.Id));
        using var scope = Scope();
        var incidencia = Assert.Single(await scope.ServiceProvider.GetRequiredService<EnviosDbContext>().Incidencias
            .Where(i => i.EnvioId == envio.Id).ToListAsync());
        Assert.Contains(envio.Bultos[1].Codigo, incidencia.Descripcion);
        Assert.DoesNotContain(envio.Bultos[0].Codigo, incidencia.Descripcion);
    }

    [Fact]
    public async Task A1_no_entrega_lo_que_no_esta_en_devolucion_ni_lo_que_todavia_no_volvio()
    {
        var enCamino = await EnDevolucionAsync(recibida: false);
        using var operario = await OperarioAsync();

        Assert.Contains("todavía no volvió al depósito", await operario.GetStringAsync($"{Pagina}?Buscar={enCamino.Numero}"));
        var respuesta = await ConfirmarAsync(operario, enCamino.Numero, enCamino.Bultos.Select(b => b.Id), "María Gómez", "12345678");
        Assert.Contains("todavía no volvió al depósito", await SeguirAsync(operario, respuesta));
        Assert.Equal(EstadoEnvio.EnDevolucion, await EstadoAsync(enCamino.Id));
        Assert.Equal(EstadoDevolucion.Pendiente, (await DevolucionAsync(enCamino.Id)).Estado);

        // Un envío que nunca tuvo devolución no se encuentra.
        Assert.Contains("No hay una devolución", await operario.GetStringAsync($"{Pagina}?Buscar=ENV-INEXISTENTE"));
    }

    // RF 31 (pendiente 2 de la decisión): un bulto vuelve al comercio solo mientras el otro del envío sigue reprogramado.
    // Sólo el bulto que recibió el comercio pasa a Devuelto; el envío no cambia y no publica EnvioDevuelto. Cuando
    // después se devuelve el resto, el envío abre otra devolución y ésa sí lo cierra.
    [Fact]
    public async Task Un_bulto_suelto_se_devuelve_solo_y_el_otro_sigue_reprogramado()
    {
        var envio = await BultoSueltoEnDevolucionAsync();
        var (reprogramado, devuelto) = (envio.Bultos.OrderBy(b => b.Codigo).First(), envio.Bultos.OrderBy(b => b.Codigo).Last());
        using var operario = await OperarioAsync();

        var pagina = await operario.GetStringAsync($"{Pagina}?Buscar={devuelto.Codigo}");
        Assert.Contains($"value=\"{devuelto.Id}\"", pagina);
        Assert.DoesNotContain($"value=\"{reprogramado.Id}\"", pagina);
        Assert.DoesNotContain("no está en devolución", WebUtility.HtmlDecode(pagina));

        // Sin el bulto devuelto falta uno: se confirma la entrega parcial (A2) y Envíos rechaza el bulto reprogramado.
        var ajeno = await ConfirmarAsync(operario, envio.Numero, [reprogramado.Id], "María Gómez", "12345678", parcial: true);
        Assert.Contains("no es de esta devolución", await SeguirAsync(operario, ajeno));

        var respuesta = await ConfirmarAsync(operario, envio.Numero, [devuelto.Id], "María Gómez", "12345678");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(Pagina, respuesta.Headers.Location?.OriginalString);
        using (var scope = Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
            var guardado = await db.Envios.AsNoTracking().SingleAsync(e => e.Id == envio.Id);
            Assert.Equal(EstadoEnvio.Reprogramado, guardado.Estado);
            Assert.Equal([EstadoEnvio.Reprogramado, EstadoEnvio.Devuelto], guardado.Bultos.OrderBy(b => b.Codigo).Select(b => b.Estado));
            Assert.False(guardado.Terminado);
            var cerrada = await db.Devoluciones.AsNoTracking().SingleAsync(d => d.EnvioId == envio.Id);
            Assert.Equal((EstadoDevolucion.Cerrada, "María Gómez"), (cerrada.Estado, cerrada.NombreReceptor));
            var eventos = (await db.Outbox.AsNoTracking().ToListAsync()).Where(m => m.Contenido.Contains(envio.Id.ToString())).ToList();
            Assert.DoesNotContain(eventos, m => m.Tipo == EnvioDevuelto.Tipo);
            Assert.Equal(devuelto.Codigo, JsonSerializer.Deserialize<BultoDevuelto>(
                Assert.Single(eventos, m => m.Tipo == BultoDevuelto.Tipo).Contenido)!.CodigoBulto);
        }

        // El despachador devuelve el resto (CU-20, T14): otra devolución, abierta, sólo con el bulto que faltaba.
        using (var scope = Scope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IniciarDevolucionHandler>().HandleAsync(
                new IniciarDevolucionCommand(envio.Id, "El comercio pidió el resto", OrigenEvento.Backoffice, null), default));
        }
        pagina = await operario.GetStringAsync($"{Pagina}?Buscar={envio.Numero}");
        Assert.Contains($"value=\"{reprogramado.Id}\"", pagina);
        Assert.DoesNotContain($"value=\"{devuelto.Id}\"", pagina);

        respuesta = await ConfirmarAsync(operario, envio.Numero, [reprogramado.Id], "Juan Pérez", "87654321");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        using (var scope = Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
            var guardado = await db.Envios.AsNoTracking().SingleAsync(e => e.Id == envio.Id);
            Assert.Equal((EstadoEnvio.Devuelto, true), (guardado.Estado, guardado.Terminado));
            Assert.All(guardado.Bultos, b => Assert.Equal(EstadoEnvio.Devuelto, b.Estado));
            Assert.Equal(["Juan Pérez", "María Gómez"], (await db.Devoluciones.AsNoTracking()
                .Where(d => d.EnvioId == envio.Id && d.Estado == EstadoDevolucion.Cerrada).ToListAsync())
                .Select(d => d.NombreReceptor!).Order());
            Assert.Empty(await db.Incidencias.Where(i => i.EnvioId == envio.Id).ToListAsync());
        }
    }

    // RF 31: de un envío entregado en parte, vuelve al comercio sólo lo que no se entregó. Los bultos que recibió el
    // destinatario no se ofrecen, no se pueden marcar y no cuentan como faltantes.
    [Fact]
    public async Task Con_bultos_ya_entregados_se_devuelve_solo_el_resto_sin_registrar_faltantes()
    {
        var envio = await EnDevolucionAsync(entregados: 2);
        var (entregado, devuelto) = (envio.Bultos[0], envio.Bultos[2]);
        using var operario = await OperarioAsync();

        var pagina = await operario.GetStringAsync($"{Pagina}?Buscar={envio.Numero}");
        Assert.Contains($"value=\"{devuelto.Id}\"", pagina);
        Assert.DoesNotContain($"value=\"{entregado.Id}\"", pagina);

        var ajeno = await ConfirmarAsync(operario, envio.Numero, [entregado.Id, devuelto.Id], "María Gómez", "12345678");
        Assert.Contains("no es de esta devolución", await SeguirAsync(operario, ajeno));
        Assert.Equal(EstadoEnvio.EnDevolucion, await EstadoAsync(envio.Id));

        var respuesta = await ConfirmarAsync(operario, envio.Numero, [devuelto.Id], "María Gómez", "12345678");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(Pagina, respuesta.Headers.Location?.OriginalString);
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var guardado = await db.Envios.AsNoTracking().SingleAsync(e => e.Id == envio.Id);
        Assert.Equal(EstadoEnvio.Devuelto, guardado.Estado);
        Assert.Equal([EstadoEnvio.Entregado, EstadoEnvio.Entregado, EstadoEnvio.Devuelto],
            guardado.Bultos.OrderBy(b => b.Codigo).Select(b => b.Estado));
        Assert.Equal((ResumenEnvio.EntregadoParcialmente, true), (guardado.Resumen, guardado.Terminado));
        Assert.Empty(await db.Incidencias.Where(i => i.EnvioId == envio.Id).ToListAsync());

        // RF 36: el cierre es mixto (los avisos v1 no lo reciben) y sólo el bulto devuelto tiene su evento.
        var eventos = (await db.Outbox.AsNoTracking().ToListAsync()).Where(m => m.Contenido.Contains(envio.Id.ToString())).ToList();
        var cierre = JsonSerializer.Deserialize<EnvioDevuelto>(Assert.Single(eventos, m => m.Tipo == EnvioDevuelto.Tipo).Contenido)!;
        Assert.Equal(2, cierre.BultosEntregados);
        var bulto = JsonSerializer.Deserialize<BultoDevuelto>(Assert.Single(eventos, m => m.Tipo == BultoDevuelto.Tipo).Contenido)!;
        Assert.Equal((devuelto.Codigo, "Devuelto"), (bulto.CodigoBulto, bulto.EstadoEnvio));
    }

    [Fact]
    public async Task Sin_el_nombre_o_el_documento_de_quien_recibe_no_cambia_nada()
    {
        var envio = await EnDevolucionAsync();
        using var operario = await OperarioAsync();

        var respuesta = await ConfirmarAsync(operario, envio.Numero, envio.Bultos.Select(b => b.Id), "María Gómez", " ");

        Assert.Contains("Completá el nombre y el documento", await SeguirAsync(operario, respuesta));
        Assert.Equal(EstadoEnvio.EnDevolucion, await EstadoAsync(envio.Id));
        Assert.Equal(EstadoDevolucion.RecibidaEnDeposito, (await DevolucionAsync(envio.Id)).Estado);
    }

    [Fact]
    public async Task Es_solo_del_operario_de_deposito_y_no_ve_las_devoluciones_de_otro_operador()
    {
        var envio = await EnDevolucionAsync();

        using var despacho = await OperarioAsync("despacho@operador-demo.uy");
        Assert.Equal(HttpStatusCode.Forbidden, (await despacho.GetAsync(Pagina)).StatusCode);

        using var otro = await OperarioAsync("deposito@envios-del-este.uy");
        Assert.Contains("No hay una devolución", await otro.GetStringAsync($"{Pagina}?Buscar={envio.Numero}"));
        var respuesta = await ConfirmarAsync(otro, envio.Numero, envio.Bultos.Select(b => b.Id), "María Gómez", "12345678");
        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal(EstadoEnvio.EnDevolucion, await EstadoAsync(envio.Id));
    }

    // Un envío del operador de demostración en devolución (T7 y T10, CU-18), con su devolución. recibida: sus bultos
    // ya volvieron en la rendición (CU-55). entregados: con entrega parcial, cuántos bultos (de un total de uno más) ya
    // recibió el destinatario; sólo el último vuelve.
    private async Task<Envio> EnDevolucionAsync(bool recibida = true, int entregados = 0)
    {
        using var inicial = factory.CreateClient();
        await inicial.GetAsync("/health");
        using var scope = Scope();
        var ahora = DateTimeOffset.UtcNow.AddMinutes(-5);
        var bultos = Enumerable.Repeat(new DatosBulto(1, 10, 10, 10, 0), entregados == 0 ? 2 : entregados + 1).ToList();
        var envio = Envio.Crear(DatosIniciales.OperadorDemoId, DatosIniciales.ComercioDemoId,
            $"CU31-{Guid.NewGuid():N}".ToUpperInvariant(), new Destinatario("Prueba Devolución", "099123456"),
            new Direccion("Calle", "10", "Montevideo", "Montevideo", "11300"), bultos, OrigenEvento.PortalComercio, null, ahora);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora);
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora);
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, ahora);
        // La parte entregada de la visita (T6 por bulto); el resto sigue con el envío a NoEntregado y EnDevolucion.
        foreach (var bulto in envio.Bultos.OrderBy(b => b.Codigo).Take(entregados))
        {
            bulto.Transicionar(EstadoEnvio.Entregado);
        }

        envio.Transicionar(EstadoEnvio.NoEntregado, OrigenEvento.AppRepartidor, null, ahora);
        envio.Transicionar(EstadoEnvio.EnDevolucion, OrigenEvento.Sistema, null, ahora);
        var devolucion = Devolucion.Crear(envio, "Intento fallido: RECHAZADO.", ahora);
        if (recibida) devolucion.MarcarRecibida(ahora);

        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        db.Envios.Add(envio);
        db.Devoluciones.Add(devolucion);
        await db.SaveChangesAsync();
        return envio;
    }

    // Lo que deja una visita mixta (RF 31): un bulto Reprogramado y el otro EnDevolucion, que ya volvió al depósito en la
    // rendición. El envío queda Reprogramado y la devolución, abierta y recibida.
    private async Task<Envio> BultoSueltoEnDevolucionAsync()
    {
        using var inicial = factory.CreateClient();
        await inicial.GetAsync("/health");
        using var scope = Scope();
        var ahora = DateTimeOffset.UtcNow.AddMinutes(-5);
        var envio = Envio.Crear(DatosIniciales.OperadorDemoId, DatosIniciales.ComercioDemoId,
            $"CU31-{Guid.NewGuid():N}".ToUpperInvariant(), new Destinatario("Prueba Devolución", "099123456"),
            new Direccion("Calle", "10", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(1, 10, 10, 10, 0), new DatosBulto(1, 10, 10, 10, 0)], OrigenEvento.PortalComercio, null, ahora);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora);
        envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora);
        envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, ahora);
        var (primero, segundo) = (envio.Bultos.OrderBy(b => b.Codigo).First(), envio.Bultos.OrderBy(b => b.Codigo).Last());
        primero.Transicionar(EstadoEnvio.NoEntregado);
        primero.Transicionar(EstadoEnvio.Reprogramado);
        segundo.Transicionar(EstadoEnvio.NoEntregado);
        segundo.Transicionar(EstadoEnvio.EnDevolucion);
        envio.Transicionar(EstadoEnvio.NoEntregado, OrigenEvento.AppRepartidor, null, ahora);
        envio.Transicionar(EstadoEnvio.Reprogramado, OrigenEvento.Sistema, null, ahora);
        var devolucion = Devolucion.Crear(envio, "Intento fallido: RECHAZADO.", ahora);
        devolucion.MarcarRecibida(ahora);

        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        db.Envios.Add(envio);
        db.Devoluciones.Add(devolucion);
        await db.SaveChangesAsync();
        return envio;
    }

    // La ruta del envío en curso, con la rendición que declaró el repartidor (CU-54): sus bultos vuelven por el intento
    // fallido.
    private async Task<Guid> RendicionConDevolucionAsync(Envio envio)
    {
        using var scope = Scope();
        var ahora = DateTimeOffset.UtcNow.AddMinutes(-5);
        var fecha = new DateOnly(2024, 9, 1).AddDays(Interlocked.Increment(ref dia));
        var ruta = Ruta.Crear(DatosIniciales.OperadorDemoId, fecha, DatosIniciales.RepartidorDemoId,
            Guid.Parse("51111111-1111-1111-1111-111111111111"), [new EnvioRuta(envio.Id, envio.Numero, fecha,
                Guid.Parse("61111111-1111-1111-1111-111111111111"), null, false,
                envio.Bultos.Select(b => new CargaBulto(envio.Id, b.Id, b.PesoKg, b.LargoCm, b.AnchoCm, b.AltoCm)).ToArray(),
                new PuntoEntrega("Calle", "10", "Montevideo", "Montevideo", "11300"))], ahora);
        ruta.Despachar(ahora);
        ruta.RegistrarCarga(true);
        var plan = scope.ServiceProvider.GetRequiredService<PlanificacionDbContext>();
        plan.Rutas.Add(ruta);
        await plan.SaveChangesAsync();

        var ejecucion = scope.ServiceProvider.GetRequiredService<EjecucionDbContext>();
        ejecucion.Rendiciones.Add(Rendicion.Iniciar(DatosIniciales.OperadorDemoId, ruta.Id, DatosIniciales.RepartidorDemoId,
            Guid.CreateVersion7(), ahora, null,
            envio.Bultos.Select(b => new DeclaracionRendicion(envio.Id, b.Id, "IntentoFallido", true, null)).ToArray()));
        await ejecucion.SaveChangesAsync();
        return ruta.Id;
    }

    // El formulario de la página, con un campo Entregados por bulto marcado.
    private static async Task<HttpResponseMessage> ConfirmarAsync(HttpClient client, string buscar, IEnumerable<Guid> entregados,
        string nombre, string documento, bool parcial = false)
    {
        var pagina = $"{Pagina}?Buscar={Uri.EscapeDataString(buscar)}";
        List<KeyValuePair<string, string>> campos =
        [
            new("__RequestVerificationToken", await Sesion.TokenAsync(client, pagina)),
            new("Buscar", buscar), new("NombreReceptor", nombre), new("DocumentoReceptor", documento),
            .. entregados.Select(id => new KeyValuePair<string, string>("Entregados", id.ToString())),
        ];
        if (parcial) campos.Add(new("ConfirmarParcial", "true"));
        return await client.PostAsync(Pagina, new FormUrlEncodedContent(campos));
    }

    // Si la confirmación no se completa, la página vuelve a la búsqueda con un GET nuevo (como CU-55).
    private static async Task<string> SeguirAsync(HttpClient client, HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        var destino = respuesta.Headers.Location!.OriginalString;
        Assert.StartsWith($"{Pagina}?Buscar=", destino);
        return WebUtility.HtmlDecode(await client.GetStringAsync(destino));
    }

    private async Task<EstadoEnvio> EstadoAsync(Guid envioId)
    {
        using var scope = Scope();
        return (await scope.ServiceProvider.GetRequiredService<EnviosDbContext>().Envios.AsNoTracking()
            .SingleAsync(e => e.Id == envioId)).Estado;
    }

    private async Task<Devolucion> DevolucionAsync(Guid envioId)
    {
        using var scope = Scope();
        return await scope.ServiceProvider.GetRequiredService<EnviosDbContext>().Devoluciones.AsNoTracking()
            .SingleAsync(d => d.EnvioId == envioId);
    }

    // Un ámbito con el operador de demostración como inquilino, como el de un request.
    private IServiceScope Scope()
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("operador_id", DatosIniciales.OperadorDemoId.ToString())], "Prueba")),
        };
        return scope;
    }

    private async Task<HttpClient> OperarioAsync(string email = "deposito@operador-demo.uy")
    {
        var client = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
            .CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(client, email);
        return client;
    }
}

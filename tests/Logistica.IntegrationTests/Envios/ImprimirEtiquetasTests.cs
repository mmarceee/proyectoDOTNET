using System.Net;
using System.Net.Http.Json;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Envios;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Envios.Application.Features.ImprimirEtiquetas;
using Logistica.Modules.Envios.Infrastructure.Etiquetas;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Envios;

public sealed class ImprimirEtiquetasTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task El_comercio_descarga_un_pdf_individual_y_un_lote_sin_modificar_el_envio()
    {
        var primero = await GuardarAsync(cantidad: 12);
        var segundo = await GuardarAsync();
        var generador = new CapturadorEtiquetas();
        using var api = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IGeneradorEtiquetas>(generador)));
        using var cliente = await ComercioAsync(api.CreateClient(Sesion.Opciones));
        var antes = await SnapshotAsync(primero);
        await EsPdfAsync(await cliente.GetAsync($"/api/envios/portal/{primero}/etiquetas"));
        await EsPdfAsync(await cliente.PostAsJsonAsync("/api/envios/portal/etiquetas",
            new ImprimirEtiquetasRequest([primero, segundo, primero])));
        Assert.Equal(14, generador.Etiquetas.Count);
        Assert.Equal(Enumerable.Range(1, 12), generador.Etiquetas.Take(12).Select(e => e.Posicion));
        Assert.All(generador.Etiquetas.Take(12), e => Assert.Equal(12, e.Total));
        Assert.Equal(segundo, generador.Etiquetas[12].NumeroEnvio);
        Assert.Equal(antes, await SnapshotAsync(primero));
        var fila = await cliente.GetFromJsonAsync<DetalleEnvioPortal>($"/api/envios/portal/{primero}");
        Assert.True(fila!.PuedeImprimirEtiquetas);
    }

    [Fact]
    public async Task Una_entrega_parcial_conserva_los_codigos_y_el_total_original_al_reimprimir()
    {
        var numero = await GuardarAsync(cantidad: 3, parcial: true);
        var generador = new CapturadorEtiquetas();
        using var api = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IGeneradorEtiquetas>(generador)));
        using var cliente = await ComercioAsync(api.CreateClient(Sesion.Opciones));
        await EsPdfAsync(await cliente.GetAsync($"/api/envios/portal/{numero}/etiquetas"));
        Assert.Equal(Enumerable.Range(1, 3).Select(i => $"{numero}-{i}"), generador.Etiquetas.Select(e => e.Codigo));
        Assert.All(generador.Etiquetas, e => Assert.Equal(3, e.Total));
    }

    [Fact]
    public async Task Inexistentes_ajenos_y_de_prueba_responden_404_tambien_en_lotes()
    {
        var propio = await GuardarAsync();
        var otroComercio = await GuardarAsync(comercio: Guid.NewGuid());
        var otroOperador = await GuardarAsync(operador: DatosIniciales.OperadorEsteId);
        var prueba = await GuardarAsync(esPrueba: true);
        using var cliente = await ComercioAsync();
        foreach (var numero in new[] { "ENV-INEXISTENTE", otroComercio, otroOperador, prueba })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"/api/envios/portal/{numero}/etiquetas")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cliente.PostAsJsonAsync("/api/envios/portal/etiquetas",
                new ImprimirEtiquetasRequest([propio, numero]))).StatusCode);
        }
    }

    [Fact]
    public async Task Cancelados_y_lotes_invalidos_se_rechazan_y_no_ofrecen_la_accion()
    {
        var propio = await GuardarAsync();
        var cancelado = await GuardarAsync(cancelado: true);
        var grande = await GuardarAsync(cantidad: 201);
        using var cliente = await ComercioAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.GetAsync($"/api/envios/portal/{cancelado}/etiquetas")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.GetAsync($"/api/envios/portal/{grande}/etiquetas")).StatusCode);
        foreach (var numeros in new IReadOnlyList<string>?[] { null, [], [" "], [propio, cancelado], Enumerable.Repeat(propio, 101).ToArray() })
            Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PostAsJsonAsync("/api/envios/portal/etiquetas",
                new ImprimirEtiquetasRequest(numeros))).StatusCode);
        var detalle = await cliente.GetFromJsonAsync<DetalleEnvioPortal>($"/api/envios/portal/{cancelado}");
        Assert.False(detalle!.PuedeImprimirEtiquetas);
        var listado = await cliente.GetFromJsonAsync<PaginaEnviosPortal>($"/api/envios/portal?texto={cancelado}");
        Assert.False(Assert.Single(listado!.Envios).PuedeImprimirEtiquetas);
    }

    [Theory]
    [InlineData("deposito@operador-demo.uy")]
    [InlineData("admin@operador-demo.uy")]
    [InlineData("despacho@operador-demo.uy")]
    public async Task El_personal_autorizado_reimprime_desde_el_detalle_con_aislamiento(string email)
    {
        var propio = await GuardarAsync();
        var ajeno = await GuardarAsync(operador: DatosIniciales.OperadorEsteId);
        var cancelado = await GuardarAsync(cancelado: true);
        using var cliente = factory.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(cliente, email);
        var html = await cliente.GetStringAsync($"/backoffice/envios/{propio}");
        Assert.Contains($"/api/envios/{propio}/etiquetas", html);
        Assert.DoesNotContain($"/api/envios/{cancelado}/etiquetas", await cliente.GetStringAsync($"/backoffice/envios/{cancelado}"));
        await EsPdfAsync(await cliente.GetAsync($"/api/envios/{propio}/etiquetas"));
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"/api/envios/{ajeno}/etiquetas")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.GetAsync($"/api/envios/{cancelado}/etiquetas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync($"/api/envios/portal/{propio}/etiquetas")).StatusCode);
    }

    [Fact]
    public async Task El_repartidor_y_el_comercio_no_pueden_usar_la_reimpresion_de_deposito()
    {
        var numero = await GuardarAsync();
        using var comercio = await ComercioAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await comercio.GetAsync($"/api/envios/{numero}/etiquetas")).StatusCode);
        using var repartidor = factory.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(repartidor, "repartidor@operador-demo.uy");
        Assert.Equal(HttpStatusCode.Forbidden, (await repartidor.GetAsync($"/api/envios/{numero}/etiquetas")).StatusCode);
        using var anonimo = factory.WithWebHostBuilder(b => b.UseSetting("Identity:PermitirTenantProvisorio", "false"))
            .CreateClient(Sesion.Opciones);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/api/envios/portal/{numero}/etiquetas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/api/envios/{numero}/etiquetas")).StatusCode);
    }

    private async Task<HttpClient> ComercioAsync(HttpClient? cliente = null)
    {
        cliente ??= factory.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(cliente, "comercio@tiendademo.uy");
        using var seleccion = await Sesion.ElegirOperadorAsync(cliente, DatosIniciales.OperadorDemoId);
        return cliente;
    }

    private static async Task EsPdfAsync(HttpResponseMessage respuesta)
    {
        using (respuesta)
        {
            Assert.True(respuesta.StatusCode == HttpStatusCode.OK,
                $"HTTP {respuesta.StatusCode}: {(respuesta.IsSuccessStatusCode ? "" : await respuesta.Content.ReadAsStringAsync())}");
            Assert.Equal("application/pdf", respuesta.Content.Headers.ContentType?.MediaType);
            Assert.True(respuesta.Headers.CacheControl?.NoStore);
            Assert.Equal("attachment", respuesta.Content.Headers.ContentDisposition?.DispositionType);
            var contenido = await respuesta.Content.ReadAsByteArrayAsync();
            Assert.True(contenido.AsSpan().StartsWith("%PDF-"u8));
        }
    }

    private async Task<string> GuardarAsync(Guid? operador = null, Guid? comercio = null, int cantidad = 2,
        bool cancelado = false, bool esPrueba = false, bool parcial = false)
    {
        using var scope = factory.Services.CreateScope();
        await using var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider,
            new InquilinoFijo(operador ?? DatosIniciales.OperadorDemoId, comercio ?? DatosIniciales.ComercioDemoId));
        var ahora = DateTimeOffset.UtcNow;
        var envio = Envio.Crear(operador ?? DatosIniciales.OperadorDemoId, comercio ?? DatosIniciales.ComercioDemoId,
            $"ENV-{Guid.NewGuid():N}", new Destinatario("Ana Etiquetas", "099000999", documento: "NO-IMPRIMIR"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300", "Puerta azul"),
            Enumerable.Repeat(new DatosBulto(1, 10, 10, 10, 100), cantidad).ToArray(),
            OrigenEvento.PortalComercio, null, ahora, esPrueba: esPrueba);
        if (cancelado) envio.Transicionar(EstadoEnvio.Cancelado, OrigenEvento.PortalComercio, null, ahora);
        if (parcial)
        {
            envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora);
            envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora);
            envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, ahora);
            var motivo = new MotivoNoEntrega(Guid.NewGuid(), "AUSENTE", reprogramable: true, exigeEvidencia: false);
            var lugar = new Ubicacion(-34.9m, -56.16m);
            var evidencia = new PruebaEntrega(Guid.NewGuid(), null, null, null, lugar, ahora);
            var resultados = envio.Bultos.Select((b, i) => new ResultadoBulto(b.Id, i == 0 ? null : motivo)).ToList();
            envio.RegistrarVisita(resultados, evidencia, null, null, lugar,
                new ReglasEntrega(false, false, 3, TimeSpan.FromHours(24), true, admiteEntregaParcial: true), null, ahora);
        }
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio.Numero;
    }

    private async Task<string> SnapshotAsync(string numero)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var envio = await db.Envios.AsNoTracking().Include(e => e.Eventos).Include(e => e.Recargos).SingleAsync(e => e.Numero == numero);
        return $"{envio.Estado}/{envio.Resumen}/{envio.Eventos.Count}/{envio.Recargos.Count}/" +
            string.Join(",", envio.Bultos.OrderBy(b => b.Codigo).Select(b => $"{b.Id}:{b.Codigo}:{b.Estado}"));
    }

    private sealed class CapturadorEtiquetas : IGeneradorEtiquetas
    {
        public IReadOnlyList<EtiquetaBulto> Etiquetas { get; private set; } = [];
        public byte[] Generar(MarcaEtiquetas marca, IReadOnlyList<EtiquetaBulto> etiquetas)
        {
            Etiquetas = etiquetas;
            return new GeneradorEtiquetasPdf().Generar(marca, etiquetas);
        }
    }
}

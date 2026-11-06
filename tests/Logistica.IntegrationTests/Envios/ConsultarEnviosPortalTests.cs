using System.Net;
using System.Net.Http.Json;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Envios;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Envios;

public sealed class ConsultarEnviosPortalTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Ruta = "/api/envios/portal";

    [Fact]
    public async Task El_portal_solo_lista_y_abre_envios_del_comercio_y_operador_de_la_sesion()
    {
        var referencia = $"CU13-{Guid.NewGuid():N}";
        var propio = await GuardarAsync(DatosIniciales.OperadorDemoId, DatosIniciales.ComercioDemoId, referencia);
        var otroComercio = await GuardarAsync(DatosIniciales.OperadorDemoId, Guid.NewGuid(), referencia);
        var otroOperador = await GuardarAsync(DatosIniciales.OperadorEsteId, DatosIniciales.ComercioDemoId, referencia);

        using var cliente = factory.CreateClient(Sesion.Opciones);
        await Sesion.IngresarAsync(cliente, "comercio@tiendademo.uy");
        using var seleccion = await Sesion.ElegirOperadorAsync(cliente, DatosIniciales.OperadorDemoId);
        Assert.Equal(HttpStatusCode.Redirect, seleccion.StatusCode);

        var pagina = await cliente.GetFromJsonAsync<PaginaEnviosPortal>($"{Ruta}?texto={referencia.ToLowerInvariant()}");
        Assert.NotNull(pagina);
        Assert.Equal(propio, Assert.Single(pagina.Envios).Numero);
        Assert.Equal(1, pagina.TotalRegistros);

        var detalle = await cliente.GetFromJsonAsync<DetalleEnvioPortal>($"{Ruta}/{propio}");
        Assert.NotNull(detalle);
        Assert.Equal(propio, detalle.Numero);
        Assert.Equal(3, detalle.Bultos.Count);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"{Ruta}/{otroComercio}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"{Ruta}/{otroOperador}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.GetAsync(
            $"{Ruta}?fechaDesde=2026-11-05&fechaHasta=2026-11-04")).StatusCode);
    }

    [Theory]
    [InlineData(false, "En curso")]
    [InlineData(true, "Finalizado")]
    public async Task Backoffice_muestra_dos_de_tres_y_distingue_si_la_entrega_parcial_termino(bool terminado, string cierre)
    {
        var referencia = $"CU13-{Guid.NewGuid():N}";
        var numero = await GuardarAsync(DatosIniciales.OperadorDemoId, DatosIniciales.ComercioDemoId, referencia, terminado);
        using var cliente = factory.CreateClient();

        var listado = WebUtility.HtmlDecode(await cliente.GetStringAsync(
            $"/backoffice/envios?Resumen=EntregadoParcialmente&ComercioId={DatosIniciales.ComercioDemoId}&Texto={referencia}"));
        Assert.Contains(numero, listado);
        Assert.Contains("Entregado parcialmente", listado);
        Assert.Contains($"2 de 3 entregados · {cierre}", listado);
        Assert.Contains(referencia, listado);
        Assert.Contains("<td>Tienda Demo SA</td>", listado);

        var detalle = WebUtility.HtmlDecode(await cliente.GetStringAsync($"/backoffice/envios/{numero}"));
        Assert.Contains($"2 de 3 entregados · {cierre}", detalle);
        Assert.Contains("Estado del bulto", detalle);
    }

    private async Task<string> GuardarAsync(Guid operador, Guid comercio, string referencia, bool? parcialTerminado = null)
    {
        using var scope = factory.Services.CreateScope();
        await using var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(
            scope.ServiceProvider, new InquilinoFijo(operador, comercio));
        var ahora = DateTimeOffset.UtcNow;
        var envio = Envio.Crear(operador, comercio, $"ENV-{Guid.NewGuid():N}",
            new Destinatario("Ana CU13", "099123456"), new Direccion("Calle", "1", "Montevideo", "Montevideo", "11300"),
            Enumerable.Repeat(new DatosBulto(1, 10, 10, 10, 150), 3).ToArray(),
            OrigenEvento.PortalComercio, null, ahora, referenciaComercio: referencia);
        if (parcialTerminado.HasValue)
        {
            envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora);
            envio.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, ahora);
            envio.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, ahora);
            var lugar = new Ubicacion(-34.9m, -56.16m);
            var firma = new PruebaEntrega(Guid.NewGuid(), null, null, null, lugar, ahora);
            var motivo = new MotivoNoEntrega(Guid.NewGuid(), "RECHAZADO", reprogramable: false, exigeEvidencia: false);
            var reglas = new ReglasEntrega(false, false, 3, TimeSpan.FromHours(24), true, admiteEntregaParcial: true);
            envio.RegistrarVisita([new(envio.Bultos[0].Id), new(envio.Bultos[1].Id), new(envio.Bultos[2].Id, motivo)],
                firma, null, null, lugar, reglas, null, ahora);
            if (parcialTerminado.Value)
                envio.Transicionar(EstadoEnvio.Devuelto, OrigenEvento.Backoffice, null, ahora);
        }

        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio.Numero;
    }
}

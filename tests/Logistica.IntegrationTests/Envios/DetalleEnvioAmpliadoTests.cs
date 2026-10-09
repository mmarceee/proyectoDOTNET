using System.Net;
using Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;
using Logistica.Modules.Envios.Domain.Devoluciones;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Domain.Incidencias;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Envios;

public sealed class DetalleEnvioAmpliadoTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private static readonly byte[] Imagen = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jD8kAAAAASUVORK5CYII=");

    [Fact]
    public async Task Persiste_y_muestra_tarifario_intentos_evidencia_incidencias_devolucion_y_ubicacion()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var datos = await PrepararAsync(db);
        await using var comercio = Contexto(scope, new Tenant(datos.Envio.OperadorId, datos.Envio.ComercioId));
        var reader = new EnvioDetalleReader(comercio);

        var detalle = await reader.ConsultarAsync(new(datos.Envio.Numero), CancellationToken.None);

        Assert.NotNull(detalle);
        Assert.Equal(4, detalle.VersionTarifario?.Numero);
        Assert.Equal(datos.Envio.VersionTarifarioId, detalle.VersionTarifario?.Id);
        Assert.Equal(2, detalle.Intentos.Count);
        Assert.Equal("Fallido", detalle.Intentos[0].Resultado);
        Assert.NotNull(detalle.Intentos[0].MotivoNoEntregaId);
        Assert.Null(detalle.Intentos[0].Evidencia);
        Assert.Equal("Exitoso", detalle.Intentos[1].Resultado);
        Assert.Equal(datos.Firma.Id, detalle.Intentos[1].Evidencia?.FirmaArchivoId);
        Assert.Equal("Receptor de prueba", detalle.Intentos[1].Evidencia?.NombreReceptor);
        Assert.Equal("12345678", detalle.Intentos[1].Evidencia?.DocumentoReceptor);
        Assert.Equal(-34m, detalle.Intentos[1].Evidencia?.Latitud);
        Assert.Equal("Reclamo de prueba", Assert.Single(detalle.Incidencias).Descripcion);
        Assert.Equal("Solicitud de prueba", detalle.Devolucion?.Motivo);
        Assert.Equal("Pendiente", detalle.Devolucion?.Estado);
        Assert.Equal(-34m, detalle.Eventos[1].Latitud);
        Assert.Equal(-56m, detalle.Eventos[1].Longitud);
        Assert.Equal("Recepción de prueba", detalle.Eventos[1].Detalle);
        Assert.Empty(comercio.ChangeTracker.Entries());

        var html = WebUtility.HtmlDecode(await factory.CreateClient().GetStringAsync($"/backoffice/envios/{datos.Envio.Numero}"));
        Assert.Contains("Versión 4", html);
        Assert.Contains("Intento 1 · Fallido", html);
        Assert.Contains("Intento 2 · Exitoso", html);
        Assert.Contains("Sin evidencia registrada", html);
        Assert.Contains("Receptor de prueba", html);
        Assert.Contains("Reclamo de prueba", html);
        Assert.Contains("Solicitud de prueba", html);
        Assert.Contains("Recepción de prueba", html);
        Assert.Contains($"/evidencias/{datos.Firma.Id}", html);
    }

    [Fact]
    public async Task La_evidencia_solo_se_sirve_si_esta_vinculada_a_un_intento_del_envio_visible()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var datos = await PrepararAsync(db);
        var client = factory.CreateClient();
        var ruta = $"/backoffice/envios/{datos.Envio.Numero}/evidencias/";

        var response = await client.GetAsync(ruta + datos.Firma.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Imagen, await response.Content.ReadAsByteArrayAsync());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(ruta + datos.SinVinculo.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(ruta + Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/backoffice/envios/OTRO/evidencias/{datos.Firma.Id}")).StatusCode);

        Tenant[] ajenos =
        [
            new(Guid.NewGuid(), null),                     // otro operador
            new(datos.Envio.OperadorId, Guid.NewGuid()),   // otro comercio del mismo operador
            new(null, null),                               // sin operador
        ];
        foreach (var tenant in ajenos)
        {
            await using var contexto = Contexto(scope, tenant);
            var reader = new ArchivoEvidenciaReader(contexto);
            Assert.Null(await reader.ConsultarAsync(datos.Envio.Numero, datos.Firma.Id, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Un_archivo_de_otro_envio_no_se_expone_aunque_el_intento_lo_referencie()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnviosDbContext>();
        var datos = await PrepararAsync(db);
        var otro = Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), $"ENV-{Guid.NewGuid():N}",
            new Destinatario("Otro", "099123456"), new Direccion("Calle", "1", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(1, 10, 10, 10, 0)], OrigenEvento.PortalComercio, null, DateTimeOffset.UtcNow);
        var archivoAjeno = ArchivoEvidencia.Crear(otro, "image/png", Imagen);

        // El otro envío es de otro operador: se guarda con un contexto de ese operador, como lo haría él.
        await using (var otroOperador = Contexto(scope, new Tenant(otro.OperadorId, null)))
        {
            otroOperador.Envios.Add(otro);
            otroOperador.ArchivosEvidencia.Add(archivoAjeno);
            await otroOperador.SaveChangesAsync();
        }

        datos.Envio.AgregarIntento(3, DateTimeOffset.UtcNow, ResultadoIntento.Fallido, Guid.NewGuid(),
            new PruebaEntrega(null, archivoAjeno.Id, null, null, new Ubicacion(-34, -56), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var response = await factory.CreateClient().GetAsync(
            $"/backoffice/envios/{datos.Envio.Numero}/evidencias/{archivoAjeno.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(Envio Envio, ArchivoEvidencia Firma, ArchivoEvidencia SinVinculo)> PrepararAsync(EnviosDbContext db)
    {
        using var scope = factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var ahora = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var envio = Envio.Crear(tenant.OperadorId!.Value, Guid.NewGuid(), $"ENV-{Guid.NewGuid():N}",
            new Destinatario("Ana", "099123456"), new Direccion("Calle", "1", "Montevideo", "Montevideo", "11300"),
            [new DatosBulto(1, 10, 10, 10, 100)], OrigenEvento.PortalComercio, null, ahora,
            new DatosVersionTarifario(Guid.NewGuid(), 4));
        var firma = ArchivoEvidencia.Crear(envio, "image/png", Imagen);
        var sinVinculo = ArchivoEvidencia.Crear(envio, "image/png", Imagen);
        envio.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, ahora.AddMinutes(1),
            new Ubicacion(-34, -56), "Recepción de prueba");
        envio.AgregarIntento(1, ahora.AddHours(1), ResultadoIntento.Fallido, Guid.NewGuid(), null, "Destinatario ausente");
        envio.AgregarIntento(2, ahora.AddHours(2), ResultadoIntento.Exitoso, null,
            new PruebaEntrega(firma.Id, null, "Receptor de prueba", "12345678", new Ubicacion(-34, -56), ahora.AddHours(2)));
        db.Envios.Add(envio);
        db.ArchivosEvidencia.AddRange(firma, sinVinculo);
        db.Incidencias.Add(Incidencia.Crear(envio, TipoIncidencia.Reclamo, "Reclamo de prueba", ahora));
        db.Devoluciones.Add(Devolucion.Crear(envio, "Solicitud de prueba", ahora));
        await db.SaveChangesAsync();
        return (envio, firma, sinVinculo);
    }

    // El filtro "Tenant" lee el inquilino del DbContext: para probar otro inquilino se arma otro contexto,
    // con el resto de las dependencias (opciones, unidad de trabajo) tomadas de la API.
    private static EnviosDbContext Contexto(IServiceScope scope, ICurrentTenant tenant)
    {
        return ActivatorUtilities.CreateInstance<EnviosDbContext>(scope.ServiceProvider, tenant);
    }

    private sealed record Tenant(Guid? OperadorId, Guid? ComercioId) : ICurrentTenant;
}

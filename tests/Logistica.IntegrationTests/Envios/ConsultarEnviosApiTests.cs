using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Logistica.BuildingBlocks.Infrastructure.Tenancy;
using Logistica.Http.Contracts.Envios;
using Logistica.IntegrationTests.Identidad;
using Logistica.Modules.Administracion.Domain.Comercios;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Logistica.IntegrationTests.Envios;

public sealed class ConsultarEnviosApiTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string Ruta = "/api/v1/envios";
    private static readonly Guid Operador = DatosIniciales.OperadorDemoId;
    private static readonly DateTimeOffset InicioDia = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Lista_filtra_y_pagina_por_fecha_local_referencia_y_resumen()
    {
        var acceso = await PrepararAccesoAsync(Operador);
        using var cliente = Cliente(acceso.Produccion);
        var ayer = await AgregarEnvioAsync(acceso, InicioDia.AddSeconds(-1), "PED-ayer");
        var primero = await AgregarEnvioAsync(acceso, InicioDia, "PED-1");
        var ultimo = await AgregarEnvioAsync(acceso, InicioDia.AddHours(2), "PED-2", EstadoEnvio.Entregado);

        var filtros = $"{Ruta}?fechaDesde=2026-11-03&fechaHasta=2026-11-03&tamanoPagina=1";
        var primera = (await cliente.GetFromJsonAsync<PaginaEnviosApiResponse>(filtros))!;
        var segunda = (await cliente.GetFromJsonAsync<PaginaEnviosApiResponse>($"{filtros}&pagina=2"))!;
        Assert.Equal((1, 1, 2), (primera.Pagina, primera.TamanoPagina, primera.Total));
        Assert.Equal(ultimo.Numero, Assert.Single(primera.Envios).Numero);
        Assert.Equal(primero.Numero, Assert.Single(segunda.Envios).Numero);

        var anterior = (await cliente.GetFromJsonAsync<PaginaEnviosApiResponse>($"{Ruta}?fechaHasta=2026-11-02"))!;
        Assert.Equal(ayer.Numero, Assert.Single(anterior.Envios).Numero);

        var filtrada = (await cliente.GetFromJsonAsync<PaginaEnviosApiResponse>(
            $"{Ruta}?referenciaComercio=%20PED-2%20&estado=entregado"))!;
        var envio = Assert.Single(filtrada.Envios);
        Assert.Equal("Entregado", envio.Estado);
        Assert.True(envio.Terminado);
        Assert.Equal((2, 2), (envio.Entregados, envio.TotalBultos));

        var referenciaDistinta = (await cliente.GetFromJsonAsync<PaginaEnviosApiResponse>(
            $"{Ruta}?referenciaComercio=ped-2"))!;
        Assert.Empty(referenciaDistinta.Envios);
        var fuera = (await cliente.GetFromJsonAsync<PaginaEnviosApiResponse>(
            $"{Ruta}?pagina={int.MaxValue}&tamanoPagina=100"))!;
        Assert.Equal((int.MaxValue, 3), (fuera.Pagina, fuera.Total));
        Assert.Empty(fuera.Envios);
    }

    [Fact]
    public async Task Detalle_proyecta_el_resultado_y_el_historial_sin_datos_internos()
    {
        var acceso = await PrepararAccesoAsync(Operador);
        var devuelto = await AgregarEnvioAsync(acceso, InicioDia, "PED-devuelto", EstadoEnvio.Devuelto);
        var cancelado = await AgregarEnvioAsync(acceso, InicioDia, "PED-cancelado", EstadoEnvio.Cancelado);
        using var cliente = Cliente(acceso.Produccion);

        using var respuesta = await cliente.GetAsync($"{Ruta}/{devuelto.Numero}");
        respuesta.EnsureSuccessStatusCode();
        var detalle = (await respuesta.Content.ReadFromJsonAsync<DetalleEnvioApiResponse>())!;
        Assert.Equal("EnOperacion", detalle.Envio.Estado);
        Assert.True(detalle.Envio.Terminado);
        Assert.Equal((0, 2, 0, 2), (detalle.Envio.Entregados, detalle.Envio.TotalBultos,
            detalle.Envio.Resultados.Pendientes, detalle.Envio.Resultados.Devueltos));
        Assert.Equal(devuelto.Bultos.Select(b => b.Codigo).Order(), detalle.Bultos.Select(b => b.Codigo));
        Assert.All(detalle.Bultos, b => Assert.Equal("Devuelto", b.Estado));
        Assert.Equal(new[] { "Admision", "RecepcionEnDeposito", "InicioDevolucion", "Devolucion" },
            detalle.Historial.Select(e => e.Tipo));
        Assert.Equal(detalle.Historial.Select(e => e.OcurridoEn).Order(), detalle.Historial.Select(e => e.OcurridoEn));

        var json = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain(acceso.ComercioId.ToString(), json);
        Assert.DoesNotContain(devuelto.Id.ToString(), json);
        Assert.All(devuelto.Bultos, b => Assert.DoesNotContain(b.Id.ToString(), json));
        Assert.DoesNotContain("nota-interna", json);
        Assert.DoesNotContain("responsableId", json);
        Assert.DoesNotContain("latitud", json);
        Assert.DoesNotContain("ana@example.test", json);

        var cierre = (await cliente.GetFromJsonAsync<DetalleEnvioApiResponse>($"{Ruta}/{cancelado.Numero}"))!;
        Assert.Equal("Creado", cierre.Envio.Estado);
        Assert.True(cierre.Envio.Terminado);
        Assert.Equal(2, cierre.Envio.Resultados.Cancelados);
    }

    [Fact]
    public async Task La_primera_recepcion_se_filtra_como_en_operacion_y_distingue_los_estados_de_los_bultos()
    {
        var acceso = await PrepararAccesoAsync(Operador);
        var envio = await AgregarEnvioAsync(acceso, InicioDia, "PED-recepcion",
            preparar: e => e.RecibirBulto(e.Bultos[0].Id, null, InicioDia.AddMinutes(1)));
        Assert.Equal(EstadoEnvio.Admitido, envio.Estado);
        using var cliente = Cliente(acceso.Produccion);

        var pagina = (await cliente.GetFromJsonAsync<PaginaEnviosApiResponse>($"{Ruta}?estado=EnOperacion"))!;
        var resumen = Assert.Single(pagina.Envios);
        Assert.Equal((envio.Numero, "EnOperacion", false, 0, 2),
            (resumen.Numero, resumen.Estado, resumen.Terminado, resumen.Entregados, resumen.TotalBultos));
        var creados = (await cliente.GetFromJsonAsync<PaginaEnviosApiResponse>($"{Ruta}?estado=Creado"))!;
        Assert.Equal(0, creados.Total);

        var detalle = (await cliente.GetFromJsonAsync<DetalleEnvioApiResponse>($"{Ruta}/{envio.Numero}"))!;
        Assert.Equal(resumen, detalle.Envio);
        Assert.Equal("EnDeposito", Assert.Single(detalle.Bultos, b => b.Codigo == envio.Bultos[0].Codigo).Estado);
        Assert.Equal("Admitido", Assert.Single(detalle.Bultos, b => b.Codigo == envio.Bultos[1].Codigo).Estado);
    }

    [Fact]
    public async Task El_listado_y_detalle_reflejan_dos_de_tres_entregados_antes_y_despues_de_la_devolucion()
    {
        var acceso = await PrepararAccesoAsync(Operador);
        var visita = InicioDia.AddHours(1);
        var envio = await AgregarEnvioAsync(acceso, InicioDia, "PED-parcial", cantidadBultos: 3, preparar: e =>
        {
            e.Transicionar(EstadoEnvio.EnDeposito, OrigenEvento.Backoffice, null, InicioDia.AddMinutes(1));
            e.Transicionar(EstadoEnvio.AsignadoARuta, OrigenEvento.Backoffice, null, InicioDia.AddMinutes(2));
            e.Transicionar(EstadoEnvio.EnTransito, OrigenEvento.AppRepartidor, null, InicioDia.AddMinutes(3));
            var motivo = new MotivoNoEntrega(Guid.NewGuid(), "AUSENTE", reprogramable: true, exigeEvidencia: false);
            var lugar = new Ubicacion(-34.9m, -56.16m);
            var firma = new PruebaEntrega(Guid.NewGuid(), null, null, null, lugar, visita);
            var reglas = new ReglasEntrega(exigeFoto: false, exigeNombreYDocumento: false, maxIntentos: 3,
                TimeSpan.FromHours(24), devolverTrasIntentosAgotados: true, admiteEntregaParcial: true);
            e.RegistrarVisita([new(e.Bultos[0].Id), new(e.Bultos[1].Id), new(e.Bultos[2].Id, motivo)],
                firma, null, null, lugar, reglas, null, visita);
        });
        var codigoPendiente = envio.Bultos[2].Codigo;
        using var cliente = Cliente(acceso.Produccion);

        await VerificarParcialAsync(false, "Reprogramado", new(1, 0, 0, 0));

        await using var scope = factory.Services.CreateAsyncScope();
        await using var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(
            scope.ServiceProvider, new InquilinoFijo(acceso.OperadorId, acceso.ComercioId));
        var persistido = await db.Envios.SingleAsync(e => e.Id == envio.Id);
        persistido.IniciarDevolucion("Venció el plazo", OrigenEvento.Backoffice, null, visita.AddDays(1));
        persistido.Transicionar(EstadoEnvio.Devuelto, OrigenEvento.Backoffice, null, visita.AddDays(2));
        await db.SaveChangesAsync();

        await VerificarParcialAsync(true, "Devuelto", new(0, 1, 0, 0));

        async Task VerificarParcialAsync(bool terminado, string estadoTercerBulto, ResultadosBultosApiResponse resultados)
        {
            var pagina = (await cliente.GetFromJsonAsync<PaginaEnviosApiResponse>(
                $"{Ruta}?estado=EntregadoParcialmente&referenciaComercio=PED-parcial"))!;
            Assert.Equal(1, pagina.Total);
            var resumen = Assert.Single(pagina.Envios);
            Assert.Equal((envio.Numero, "EntregadoParcialmente", terminado, 2, 3),
                (resumen.Numero, resumen.Estado, resumen.Terminado, resumen.Entregados, resumen.TotalBultos));
            Assert.Equal(resultados, resumen.Resultados);

            var detalle = (await cliente.GetFromJsonAsync<DetalleEnvioApiResponse>($"{Ruta}/{envio.Numero}"))!;
            Assert.Equal(resumen, detalle.Envio);
            Assert.Equal(3, detalle.Bultos.Count);
            Assert.Equal(estadoTercerBulto, Assert.Single(detalle.Bultos, b => b.Codigo == codigoPendiente).Estado);
            Assert.All(detalle.Bultos.Where(b => b.Codigo != codigoPendiente), b => Assert.Equal("Entregado", b.Estado));
        }
    }

    [Fact]
    public async Task Solo_lee_la_relacion_y_el_ambiente_de_la_clave()
    {
        var propia = await PrepararAccesoAsync(Operador);
        var otroComercio = await PrepararAccesoAsync(Operador);
        var otroOperador = await PrepararAccesoAsync(DatosIniciales.OperadorEsteId, propia.ComercioId);
        var real = await AgregarEnvioAsync(propia, InicioDia, "PED-comun");
        var prueba = await AgregarEnvioAsync(propia, InicioDia, "PED-comun", esPrueba: true);
        var ajeno = await AgregarEnvioAsync(otroComercio, InicioDia, "PED-comun");
        var deOtroOperador = await AgregarEnvioAsync(otroOperador, InicioDia, "PED-comun");
        using var produccion = Cliente(propia.Produccion);
        using var pruebas = Cliente(propia.Prueba);

        var lista = (await produccion.GetFromJsonAsync<PaginaEnviosApiResponse>(Ruta))!;
        Assert.Equal(real.Numero, Assert.Single(lista.Envios).Numero);
        lista = (await pruebas.GetFromJsonAsync<PaginaEnviosApiResponse>(Ruta))!;
        Assert.Equal(prueba.Numero, Assert.Single(lista.Envios).Numero);
        Assert.Equal(prueba.Numero, (await pruebas.GetFromJsonAsync<DetalleEnvioApiResponse>($"{Ruta}/{prueba.Numero}"))!.Envio.Numero);

        foreach (var numero in new[] { prueba.Numero, ajeno.Numero, deOtroOperador.Numero, "ENV-INEXISTENTE" })
        {
            using var respuesta = await produccion.GetAsync($"{Ruta}/{numero}");
            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        }
        using var realDesdePruebas = await pruebas.GetAsync($"{Ruta}/{real.Numero}");
        Assert.Equal(HttpStatusCode.NotFound, realDesdePruebas.StatusCode);

        using var sinClave = factory.CreateClient(Sesion.Opciones);
        using var listaNoAutorizada = await sinClave.GetAsync(Ruta);
        using var detalleNoAutorizado = await sinClave.GetAsync($"{Ruta}/{real.Numero}");
        Assert.Equal(HttpStatusCode.Unauthorized, listaNoAutorizada.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, detalleNoAutorizado.StatusCode);
    }

    [Fact]
    public async Task Suspender_la_relacion_rechaza_la_clave_y_no_actualiza_su_uso()
    {
        var acceso = await PrepararAccesoAsync(Operador);
        var envio = await AgregarEnvioAsync(acceso, InicioDia, "PED-suspendido");
        await using var scope = factory.Services.CreateAsyncScope();
        await using var db = ActivatorUtilities.CreateInstance<AdministracionDbContext>(
            scope.ServiceProvider, new InquilinoFijo(Operador, acceso.ComercioId));
        var relacion = await db.RelacionesComerciales.SingleAsync();
        relacion.Suspender();
        await db.SaveChangesAsync();

        using var cliente = Cliente(acceso.Produccion);
        using var respuesta = await cliente.GetAsync($"{Ruta}/{envio.Numero}");
        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.All(await db.ClavesApi.AsNoTracking().ToListAsync(), clave => Assert.Null(clave.UltimoUsoEn));
    }

    [Fact]
    public async Task Filtros_invalidos_responden_400_y_el_estado_parcial_tiene_un_nombre_estable()
    {
        var acceso = await PrepararAccesoAsync(Operador);
        using var cliente = Cliente(acceso.Produccion);
        foreach (var filtro in new[]
        {
            "pagina=0", "tamanoPagina=101", "estado=Admitido", "estado=1", "fechaDesde=mal",
            "fechaDesde=2026-11-04&fechaHasta=2026-11-03", $"referenciaComercio={new string('A', 51)}",
        })
        {
            using var respuesta = await cliente.GetAsync($"{Ruta}?{filtro}");
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.Equal(400, (await respuesta.Content.ReadFromJsonAsync<ProblemDetails>())?.Status);
        }

        var parciales = (await cliente.GetFromJsonAsync<PaginaEnviosApiResponse>(
            $"{Ruta}?estado=EntregadoParcialmente"))!;
        Assert.Empty(parciales.Envios);
    }

    private HttpClient Cliente(string clave)
    {
        var cliente = factory.CreateClient(Sesion.Opciones);
        cliente.DefaultRequestHeaders.Add("X-Api-Key", clave);
        return cliente;
    }

    private async Task<Acceso> PrepararAccesoAsync(Guid operador, Guid? comercioExistente = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var comercio = comercioExistente is null ? Comercio.Crear("Tienda CU-82", Guid.NewGuid().ToString("N")[..12]) : null;
        var comercioId = comercioExistente ?? comercio!.Id;
        await using var db = ActivatorUtilities.CreateInstance<AdministracionDbContext>(
            scope.ServiceProvider, new InquilinoFijo(operador, comercioId));
        if (comercio is not null) db.Comercios.Add(comercio);
        var relacion = RelacionComercial.Crear(operador, comercioId, "cu82@example.test", InicioDia);
        db.RelacionesComerciales.Add(relacion);
        var produccion = CrearClave(TipoClaveApi.Produccion);
        var prueba = CrearClave(TipoClaveApi.Prueba);
        await db.SaveChangesAsync();
        return new(operador, comercioId, produccion, prueba);

        string CrearClave(TipoClaveApi tipo)
        {
            var secreto = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secreto)));
            db.ClavesApi.Add(ClaveApi.Crear(relacion, "CU-82", tipo, hash, secreto[..ClaveApi.LargoPrefijo], InicioDia));
            return secreto;
        }
    }

    private async Task<Envio> AgregarEnvioAsync(Acceso acceso, DateTimeOffset fecha, string referencia,
        EstadoEnvio estado = EstadoEnvio.Admitido, bool esPrueba = false,
        int cantidadBultos = 2, Action<Envio>? preparar = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await using var db = ActivatorUtilities.CreateInstance<EnviosDbContext>(
            scope.ServiceProvider, new InquilinoFijo(acceso.OperadorId, acceso.ComercioId));
        var envio = Envio.Crear(acceso.OperadorId, acceso.ComercioId, $"ENV-{Guid.NewGuid():N}",
            new Destinatario("Ana Pérez", "099123456", "ana@example.test"),
            new Direccion("Av. Italia", "1234", "Montevideo", "Montevideo", "11300"),
            Enumerable.Repeat(new DatosBulto(2, 30, 20, 10, 150), cantidadBultos).ToArray(), OrigenEvento.Api, null,
            fecha, referenciaComercio: referencia, esPrueba: esPrueba);
        var pasos = estado switch
        {
            EstadoEnvio.Entregado => new[] { EstadoEnvio.EnDeposito, EstadoEnvio.AsignadoARuta, EstadoEnvio.EnTransito, estado },
            EstadoEnvio.Devuelto => new[] { EstadoEnvio.EnDeposito, EstadoEnvio.EnDevolucion, estado },
            EstadoEnvio.Cancelado => new[] { estado },
            _ => [],
        };
        foreach (var paso in pasos)
        {
            fecha = fecha.AddMinutes(1);
            envio.Transicionar(paso, OrigenEvento.Backoffice, Guid.NewGuid(), fecha, detalle: "nota-interna");
        }
        preparar?.Invoke(envio);
        db.Envios.Add(envio);
        await db.SaveChangesAsync();
        return envio;
    }

    private sealed record Acceso(Guid OperadorId, Guid ComercioId, string Produccion, string Prueba);
}

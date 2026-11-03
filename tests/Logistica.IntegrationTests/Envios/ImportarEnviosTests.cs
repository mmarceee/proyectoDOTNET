using System.Net;
using System.Net.Http.Json;
using System.Text;
using Logistica.Http.Contracts.Envios;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Logistica.IntegrationTests.Envios;

// CU-11 de punta a punta: archivo, vista previa, confirmación y la idempotencia de RF 7 contra PostgreSQL.
public class ImportarEnviosTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    private const string VistaPrevia = "/api/envios/importacion/vista-previa";
    private const string Confirmar = "/api/envios/importacion";

    private const string Encabezado =
        "Referencia;Nombre;Telefono;Email;Documento;Calle;NumeroPuerta;Localidad;Departamento;CodigoPostal;" +
        "ReferenciaDireccion;Modalidad;PesoKg;LargoCm;AnchoCm;AltoCm";

    private static string Fila(string referencia, string peso = "2", string codigoPostal = "11300", string calle = "Av. Italia") =>
        $"{referencia};Ana Pérez;099123456;;;{calle};1234;Montevideo;Montevideo;{codigoPostal};;;{peso};30;20;10";

    private static ImportarEnviosRequest Archivo(params string[] filas) =>
        new(Encoding.UTF8.GetBytes(string.Join("\r\n", [Encabezado, .. filas])));

    // Las pruebas de la clase comparten la base: cada una usa referencias propias.
    private static string NuevaReferencia() => $"PED-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task La_vista_previa_cotiza_sin_crear_y_confirmar_crea_cada_envio_una_sola_vez()
    {
        var client = factory.CreateClient();
        var (dosBultos, unBulto) = (NuevaReferencia(), NuevaReferencia());
        var archivo = Archivo(Fila(dosBultos), Fila(dosBultos, peso: "5"), Fila(unBulto));

        var previa = await ProcesarAsync(client, VistaPrevia, archivo);
        Assert.True(previa.PuedeConfirmar);
        Assert.Equal(("ACrear", 450m), Resultado(previa, dosBultos));
        Assert.Equal(("ACrear", 150m), Resultado(previa, unBulto));
        Assert.Equal([2, 3], previa.Envios.Single(e => e.Referencia == dosBultos).Filas);
        Assert.Equal(0L, await ContarEnviosAsync(dosBultos));

        var creados = await ProcesarAsync(client, Confirmar, archivo);
        Assert.Equal(("Creado", 450m), Resultado(creados, dosBultos));
        Assert.StartsWith("ENV-", creados.Envios.Single(e => e.Referencia == dosBultos).Numero);
        Assert.Equal(1L, await ContarEnviosAsync(dosBultos));
        Assert.Equal(2L, await ConsultarAsync<long>(
            """SELECT count(*) FROM envios."Bultos" b JOIN envios."Envios" e ON e."Id" = b."EnvioId" WHERE e."ReferenciaComercio" = @ref""",
            dosBultos));
        Assert.Equal(1L, await ContarEventosAdmitidoAsync(dosBultos));

        // A3: reimportar el mismo archivo no crea nada y lo informa.
        var otraVez = await ProcesarAsync(client, VistaPrevia, archivo);
        Assert.False(otraVez.PuedeConfirmar);
        Assert.All(otraVez.Envios, e => Assert.Equal("YaExistente", e.Resultado));
        var reconfirmado = await ProcesarAsync(client, Confirmar, archivo);
        Assert.Equal(creados.Envios.Single(e => e.Referencia == dosBultos).Numero,
            reconfirmado.Envios.Single(e => e.Referencia == dosBultos).Numero);
        Assert.Equal(1L, await ContarEnviosAsync(dosBultos));
        Assert.Equal(1L, await ContarEventosAdmitidoAsync(dosBultos));
    }

    [Fact]
    public async Task Reimportar_un_archivo_ampliado_crea_solo_los_envios_que_faltan()
    {
        var client = factory.CreateClient();
        var (anterior, nuevo) = (NuevaReferencia(), NuevaReferencia());
        await ProcesarAsync(client, Confirmar, Archivo(Fila(anterior)));

        var resultado = await ProcesarAsync(client, Confirmar, Archivo(Fila(anterior), Fila(nuevo)));

        Assert.Equal("YaExistente", Resultado(resultado, anterior).Resultado);
        Assert.Equal("Creado", Resultado(resultado, nuevo).Resultado);
        Assert.Equal(1L, await ContarEnviosAsync(anterior));
        Assert.Equal(1L, await ContarEnviosAsync(nuevo));
    }

    [Fact]
    public async Task Un_envio_con_errores_no_impide_crear_los_demas()
    {
        var client = factory.CreateClient();
        var (valido, sinCobertura, inconsistente) = (NuevaReferencia(), NuevaReferencia(), NuevaReferencia());
        var archivo = Archivo(
            Fila(valido), Fila(sinCobertura, codigoPostal: "99999"),
            Fila(inconsistente), Fila(inconsistente, calle: "18 de Julio"));

        var resultado = await ProcesarAsync(client, Confirmar, archivo);

        Assert.Equal("Creado", Resultado(resultado, valido).Resultado);
        var fueraDeZona = resultado.Envios.Single(e => e.Referencia == sinCobertura);
        Assert.Equal("Error", fueraDeZona.Resultado);
        Assert.Contains("no cubre el código postal", fueraDeZona.Detalle);
        var distintos = resultado.Envios.Single(e => e.Referencia == inconsistente);
        Assert.Equal("Error", distintos.Resultado);
        Assert.Contains("Las filas 4, 5", distintos.Detalle);
        Assert.Equal(1L, await ContarEnviosAsync(valido));
        Assert.Equal(0L, await ContarEnviosAsync(sinCobertura));
        Assert.Equal(0L, await ContarEnviosAsync(inconsistente));
    }

    [Fact]
    public async Task A2_si_ningun_envio_es_valido_no_se_ofrece_confirmar()
    {
        var client = factory.CreateClient();

        var previa = await ProcesarAsync(client, VistaPrevia, Archivo(Fila(NuevaReferencia(), codigoPostal: "99999")));

        Assert.False(previa.PuedeConfirmar);
        Assert.Equal("Error", Assert.Single(previa.Envios).Resultado);
    }

    [Fact]
    public async Task A1_un_archivo_con_columnas_faltantes_se_rechaza_entero_con_el_motivo()
    {
        var client = factory.CreateClient();
        var referencia = NuevaReferencia();
        var archivo = new ImportarEnviosRequest(Encoding.UTF8.GetBytes(
            $"{Encabezado.Replace(";PesoKg", "")}\r\n{referencia};Ana Pérez;099123456;;;Av. Italia;1234;Montevideo;Montevideo;11300;;;30;20;10"));

        var response = await client.PostAsJsonAsync(Confirmar, archivo);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("PesoKg", (await response.Content.ReadFromJsonAsync<ProblemDetails>())?.Detail);
        Assert.Equal(0L, await ContarEnviosAsync(referencia));
    }

    [Fact]
    public async Task A4_dos_importaciones_simultaneas_del_mismo_archivo_crean_cada_envio_una_sola_vez()
    {
        var referencias = Enumerable.Range(0, 8).Select(_ => NuevaReferencia()).ToArray();
        var archivo = Archivo(referencias.Select(r => Fila(r)).ToArray());

        var resultados = await Task.WhenAll(
            ProcesarAsync(factory.CreateClient(), Confirmar, archivo),
            ProcesarAsync(factory.CreateClient(), Confirmar, archivo));

        foreach (var referencia in referencias)
        {
            Assert.Equal(1L, await ContarEnviosAsync(referencia));
            Assert.Equal(1L, await ContarEventosAdmitidoAsync(referencia));
            // Una importación lo creó y la otra lo informó como existente, con el mismo número.
            var deEstaReferencia = resultados.Select(r => r.Envios.Single(e => e.Referencia == referencia)).ToList();
            Assert.Equal(["Creado", "YaExistente"], deEstaReferencia.Select(e => e.Resultado).Order());
            Assert.Single(deEstaReferencia.Select(e => e.Numero).Distinct());
        }
    }

    private static async Task<ImportarEnviosResponse> ProcesarAsync(HttpClient client, string ruta, ImportarEnviosRequest archivo)
    {
        var response = await client.PostAsJsonAsync(ruta, archivo);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportarEnviosResponse>())!;
    }

    private static (string Resultado, decimal? Tarifa) Resultado(ImportarEnviosResponse respuesta, string referencia)
    {
        var envio = respuesta.Envios.Single(e => e.Referencia == referencia);
        return (envio.Resultado, envio.Tarifa);
    }

    private Task<long> ContarEnviosAsync(string referencia) =>
        ConsultarAsync<long>("""SELECT count(*) FROM envios."Envios" WHERE "ReferenciaComercio" = @ref""", referencia);

    // T1 llega a Seguimiento por el outbox: un evento por envío creado, aunque se importe dos veces.
    private Task<long> ContarEventosAdmitidoAsync(string referencia) => ConsultarAsync<long>("""
        SELECT count(*) FROM envios.outbox_messages o
        JOIN envios."Envios" e ON o."Contenido"::jsonb ->> 'EnvioId' = e."Id"::text
        WHERE o."Tipo" = 'EnvioAdmitido.v1' AND e."ReferenciaComercio" = @ref
        """, referencia);

    private async Task<T> ConsultarAsync<T>(string sql, string referencia)
    {
        await using var conexion = new NpgsqlConnection(factory.ConnectionString);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("ref", referencia);
        return (T)(await comando.ExecuteScalarAsync())!;
    }
}

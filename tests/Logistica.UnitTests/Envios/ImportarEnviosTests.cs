using System.Text;
using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Envios.Application.Features.ImportarEnvios;
using Logistica.Modules.Envios.Presentation.Features.ImportarEnvios;
using Logistica.SharedKernel;

namespace Logistica.UnitTests.Envios;

// CU-11: la lectura del archivo (paso 3 y A1) y el armado de cada envío a partir de sus filas (paso 4 y A5).
public class ImportarEnviosTests
{
    private const string Encabezado =
        "Referencia;Nombre;Telefono;Email;Documento;Calle;NumeroPuerta;Localidad;Departamento;CodigoPostal;" +
        "ReferenciaDireccion;Modalidad;PesoKg;LargoCm;AnchoCm;AltoCm";

    private static string Fila(string referencia, string peso = "2", string calle = "Av. Italia", string modalidad = "") =>
        $"{referencia};Ana Pérez;099123456;;;{calle};1234;Montevideo;Montevideo;11300;;{modalidad};{peso};30;20;10";

    private static byte[] Archivo(params string[] lineas) => Encoding.UTF8.GetBytes(string.Join("\r\n", lineas));

    [Fact]
    public void Lee_una_fila_por_bulto_con_el_numero_de_fila_de_la_planilla()
    {
        // Una planilla guarda la fila vacía como separadores sin datos: no es un envío, pero cuenta como fila.
        var filas = LectorArchivoEnvios.Leer(Archivo(Encabezado, Fila("A-1"), ";;;;;;;;;;;;;;;", Fila("A-2")));

        Assert.Equal([2, 4], filas.Select(f => f.Numero));
        Assert.Equal("A-1", filas[0].Referencia);
        Assert.Equal("Av. Italia", filas[0].Calle);
    }

    [Fact]
    public void Acepta_la_coma_como_separador_y_los_campos_entre_comillas()
    {
        var encabezado = Encabezado.Replace(';', ',');
        var fila = "A-1,Ana Pérez,099123456,,,\"Av. Italia, esquina Bolivia\",1234,Montevideo,Montevideo,11300,,,2.5,30,20,10";

        var unica = Assert.Single(LectorArchivoEnvios.Leer(Archivo(encabezado, fila)));

        Assert.Equal("Av. Italia, esquina Bolivia", unica.Calle);
        Assert.Equal("2.5", unica.PesoKg);
    }

    [Fact]
    public void Ignora_la_marca_de_orden_de_bytes_que_agrega_una_planilla()
    {
        var conMarca = Encoding.UTF8.GetPreamble().Concat(Archivo(Encabezado, Fila("A-1"))).ToArray();

        Assert.Single(LectorArchivoEnvios.Leer(conMarca));
    }

    [Fact]
    public void Un_archivo_que_no_esta_en_UTF8_se_rechaza_entero()
    {
        var latin1 = Encoding.Latin1.GetBytes(string.Join("\r\n", Encabezado, Fila("A-1")));

        var error = Assert.Throws<DomainException>(() => LectorArchivoEnvios.Leer(latin1));
        Assert.Contains("UTF-8", error.Message);
    }

    [Fact]
    public void Un_archivo_sin_alguna_columna_se_rechaza_e_indica_cuales_faltan()
    {
        var error = Assert.Throws<DomainException>(() =>
            LectorArchivoEnvios.Leer(Archivo(Encabezado.Replace(";PesoKg", "").Replace(";Calle", ""), "A-1")));

        Assert.Contains("Calle, PesoKg", error.Message);
    }

    [Fact]
    public void Un_archivo_de_mas_de_1000_filas_se_rechaza()
    {
        var lineas = new[] { Encabezado }.Concat(Enumerable.Range(1, 1001).Select(i => Fila($"A-{i}"))).ToArray();

        Assert.Equal(1000, LectorArchivoEnvios.Leer(Archivo(lineas[..1001])).Count);
        var error = Assert.Throws<DomainException>(() => LectorArchivoEnvios.Leer(Archivo(lineas)));
        Assert.Contains("1000 filas", error.Message);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(null)]
    public void Sin_archivo_o_con_un_archivo_vacio_no_hay_nada_que_importar(byte[]? archivo)
    {
        Assert.Throws<DomainException>(() => LectorArchivoEnvios.Leer(archivo));
        Assert.Throws<DomainException>(() => LectorArchivoEnvios.Leer(Archivo(Encabezado)));
    }

    [Fact]
    public void Las_filas_con_la_misma_referencia_forman_un_envio_con_un_bulto_cada_una()
    {
        var filas = LectorArchivoEnvios.Leer(Archivo(Encabezado, Fila("A-1", peso: "2,5"), Fila("A-1", peso: "1")));

        var alta = ImportarEnviosHandler.ArmarEnvio("A-1", filas);

        Assert.Equal("A-1", alta.ReferenciaComercio);
        Assert.Equal([2.5m, 1m], alta.Bultos.Select(b => b.PesoKg));
        Assert.Equal(Modalidad.Estandar, alta.Modalidad);
        Assert.Null(alta.Destinatario.Email);
    }

    [Fact]
    public void A5_la_misma_referencia_con_otra_direccion_marca_el_envio_con_error()
    {
        var filas = LectorArchivoEnvios.Leer(Archivo(Encabezado, Fila("A-1"), Fila("A-1", calle: "18 de Julio")));

        var error = Assert.Throws<DomainException>(() => ImportarEnviosHandler.ArmarEnvio("A-1", filas));
        Assert.Contains("Las filas 2, 3", error.Message);
    }

    [Theory]
    [InlineData("", "2", "referencia del comercio")]
    [InlineData("A-1", "dos", "el peso")]
    [InlineData("A-1", "", "el peso")]
    public void Una_fila_sin_referencia_o_con_un_numero_invalido_marca_el_envio_con_error(
        string referencia, string peso, string motivo)
    {
        var filas = LectorArchivoEnvios.Leer(Archivo(Encabezado, Fila(referencia, peso)));

        var error = Assert.Throws<DomainException>(() => ImportarEnviosHandler.ArmarEnvio(referencia, filas));
        Assert.Contains(motivo, error.Message);
    }

    [Theory]
    [InlineData("Urgente", Modalidad.Urgente)]
    [InlineData("estándar", Modalidad.Estandar)]
    [InlineData("ESTANDAR", Modalidad.Estandar)]
    public void La_modalidad_se_lee_sin_importar_mayusculas_ni_tildes(string texto, Modalidad esperada)
    {
        var filas = LectorArchivoEnvios.Leer(Archivo(Encabezado, Fila("A-1", modalidad: texto)));

        Assert.Equal(esperada, ImportarEnviosHandler.ArmarEnvio("A-1", filas).Modalidad);
    }

    [Fact]
    public void Una_modalidad_desconocida_marca_el_envio_con_error()
    {
        var filas = LectorArchivoEnvios.Leer(Archivo(Encabezado, Fila("A-1", modalidad: "Express")));

        Assert.Throws<DomainException>(() => ImportarEnviosHandler.ArmarEnvio("A-1", filas));
    }
}

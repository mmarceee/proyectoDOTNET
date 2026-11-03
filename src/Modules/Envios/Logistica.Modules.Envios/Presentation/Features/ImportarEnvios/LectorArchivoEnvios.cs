using System.Text;
using Logistica.Modules.Envios.Application.Features.ImportarEnvios;
using Logistica.SharedKernel;
using Microsoft.VisualBasic.FileIO;

namespace Logistica.Modules.Envios.Presentation.Features.ImportarEnvios;

// CU-11, paso 3: traduce el archivo CSV a filas y valida su estructura. Si algo falla, se rechaza el archivo completo
// (A1). TextFieldParser, que viene con .NET, respeta los campos entre comillas, como una dirección con comas.
internal static class LectorArchivoEnvios
{
    public const int MaximoFilas = 1000;
    public const int MaximoBytes = 1024 * 1024;

    // El encabezado de la plantilla. El orden puede cambiar; los nombres no distinguen mayúsculas.
    public static readonly string[] Columnas =
    [
        "Referencia", "Nombre", "Telefono", "Email", "Documento", "Calle", "NumeroPuerta", "Localidad", "Departamento",
        "CodigoPostal", "ReferenciaDireccion", "Modalidad", "PesoKg", "LargoCm", "AnchoCm", "AltoCm",
    ];

    private static readonly UTF8Encoding Utf8Estricto = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static IReadOnlyList<FilaImportacion> Leer(byte[]? archivo)
    {
        if (archivo is null || archivo.Length == 0)
            throw new DomainException("Elegí un archivo CSV con los envíos.");
        if (archivo.Length > MaximoBytes)
            throw new DomainException("El archivo supera 1 MB. Dividilo en varios archivos.");

        string texto;
        try { texto = Utf8Estricto.GetString(archivo).TrimStart('﻿'); }
        catch (DecoderFallbackException)
        { throw new DomainException("El archivo no está en UTF-8. Guardalo como «CSV UTF-8» e intentá de nuevo."); }

        // Una planilla en español separa con punto y coma; otras herramientas, con coma. Lo decide el encabezado.
        var finEncabezado = texto.IndexOfAny(['\r', '\n']);
        var separador = (finEncabezado < 0 ? texto : texto[..finEncabezado]).Contains(';') ? ";" : ",";

        using var parser = new TextFieldParser(new StringReader(texto))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false,
        };
        parser.SetDelimiters(separador);

        var encabezado = parser.ReadFields()
            ?? throw new DomainException("El archivo está vacío.");
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < encabezado.Length; i++)
            indices.TryAdd(encabezado[i].Trim(), i);
        var faltantes = Columnas.Where(c => !indices.ContainsKey(c)).ToList();
        if (faltantes.Count > 0)
            throw new DomainException($"Faltan columnas en el archivo: {string.Join(", ", faltantes)}. Usá la plantilla.");

        var filas = new List<FilaImportacion>();
        var numero = 1;
        while (!parser.EndOfData)
        {
            string[]? campos;
            try { campos = parser.ReadFields(); }
            catch (MalformedLineException)
            { throw new DomainException($"La fila {numero + 1} tiene comillas sin cerrar."); }

            // El número de fila de la planilla, para el informe. Una fila vacía de planilla («;;;») cuenta y se saltea;
            // TextFieldParser ya saltea, sin contarlas, las líneas totalmente en blanco.
            numero++;
            if (campos is null || campos.All(string.IsNullOrWhiteSpace))
                continue;
            if (filas.Count == MaximoFilas)
                throw new DomainException($"El archivo supera las {MaximoFilas} filas. Dividilo en varios archivos.");

            string Campo(string columna) => indices[columna] < campos.Length ? campos[indices[columna]] : "";
            filas.Add(new FilaImportacion(numero,
                Campo("Referencia"), Campo("Nombre"), Campo("Telefono"), Campo("Email"), Campo("Documento"),
                Campo("Calle"), Campo("NumeroPuerta"), Campo("Localidad"), Campo("Departamento"), Campo("CodigoPostal"),
                Campo("ReferenciaDireccion"), Campo("Modalidad"),
                Campo("PesoKg"), Campo("LargoCm"), Campo("AnchoCm"), Campo("AltoCm")));
        }

        if (filas.Count == 0)
            throw new DomainException("El archivo no tiene envíos: sólo el encabezado.");
        return filas;
    }
}

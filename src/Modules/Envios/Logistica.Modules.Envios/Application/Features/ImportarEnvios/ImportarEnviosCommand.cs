namespace Logistica.Modules.Envios.Application.Features.ImportarEnvios;

// CU-11: las filas del archivo, ya leídas y con su estructura validada (A1). Cada fila es un bulto; las filas con la
// misma referencia del comercio forman un envío. Sin confirmar, el handler sólo arma la vista previa.
internal sealed record ImportarEnviosCommand(IReadOnlyList<FilaImportacion> Filas, bool Confirmar);

// Una fila tal como viene en el archivo: texto sin interpretar, salvo su número (la fila 1 es el encabezado).
internal sealed record FilaImportacion(
    int Numero,
    string Referencia,
    string Nombre,
    string Telefono,
    string Email,
    string Documento,
    string Calle,
    string NumeroPuerta,
    string Localidad,
    string Departamento,
    string CodigoPostal,
    string ReferenciaDireccion,
    string Modalidad,
    string PesoKg,
    string LargoCm,
    string AnchoCm,
    string AltoCm);

internal enum ResultadoImportacion
{
    ACrear,
    Creado,
    YaExistente,
    Error,
}

// El resultado de un envío del archivo, con las filas que lo forman, para el informe por fila (paso 9).
internal sealed record EnvioImportado(
    string Referencia, IReadOnlyList<int> Filas, ResultadoImportacion Resultado, string? Numero, decimal? Tarifa,
    string? Detalle);

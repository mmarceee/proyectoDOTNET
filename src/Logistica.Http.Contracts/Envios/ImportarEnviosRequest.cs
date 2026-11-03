namespace Logistica.Http.Contracts.Envios;

// Cuerpo de POST /api/envios/importacion/vista-previa y /api/envios/importacion (CU-11). El archivo CSV viaja en
// base64 dentro del JSON: la API no guarda la vista previa, así que confirmar vuelve a enviar el mismo archivo.
public sealed record ImportarEnviosRequest(byte[] Archivo);

// Un envío por referencia del comercio, con las filas del archivo que lo forman. Resultado: ACrear (vista previa),
// Creado, YaExistente o Error. PuedeConfirmar es falso si no queda ningún envío por crear (A2).
public sealed record ImportarEnviosResponse(IReadOnlyList<EnvioImportadoResponse> Envios, bool PuedeConfirmar);

public sealed record EnvioImportadoResponse(
    string Referencia, IReadOnlyList<int> Filas, string Resultado, string? Numero, decimal? Tarifa, string? Detalle);

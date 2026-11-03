namespace Logistica.Modules.Envios.Application.Features.ConsultarEnvios;

internal sealed record EnvioListadoDto(
    string Numero,
    Guid ComercioId,
    string DestinatarioNombre,
    string Localidad,
    string Estado,
    DateTimeOffset CreadoEn,
    decimal MontoTarifa,
    string Modalidad = "Estandar",
    // RF 31: el resumen derivado de los bultos, si ya terminó y el contador entregados / total.
    string Resumen = "Creado",
    bool Terminado = false,
    int BultosEntregados = 0,
    int TotalBultos = 0);

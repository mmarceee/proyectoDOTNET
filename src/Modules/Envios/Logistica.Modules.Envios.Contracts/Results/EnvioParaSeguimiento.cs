namespace Logistica.Modules.Envios.Contracts.Results;

// CU-60: una fila del listado de la página de seguimiento del Backoffice, para que el personal elija el envío cuyo
// enlace quiere mandar. Es para el personal del operador, no para el público: por eso lleva el nombre completo.
public sealed record EnvioParaSeguimiento(
    string Numero, string Estado, string Destinatario, string Localidad, DateTimeOffset CreadoEn);

// Una página del listado, con lo necesario para mostrar "anterior" y "siguiente".
public sealed record PaginaEnviosParaSeguimiento(
    IReadOnlyList<EnvioParaSeguimiento> Envios, int Pagina, int TotalPaginas);

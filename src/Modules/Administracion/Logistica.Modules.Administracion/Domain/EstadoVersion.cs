namespace Logistica.Modules.Administracion.Domain;

// Estado de una versión del cuadro tarifario (CU-05) o de las reglas operativas (CU-06). Una versión publicada no se
// modifica (letra 6.6). Una Vigente con fecha de inicio futura se muestra como "programada": cuál rige un día lo deciden
// las fechas de vigencia, no el estado.
internal enum EstadoVersion
{
    Borrador,
    Vigente,
    Reemplazada,
}

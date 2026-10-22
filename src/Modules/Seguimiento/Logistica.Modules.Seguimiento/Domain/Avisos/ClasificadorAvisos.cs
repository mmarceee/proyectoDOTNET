namespace Logistica.Modules.Seguimiento.Domain.Avisos;

// Cada hecho del envío se clasifica en un único tipo: así una suscripción no recibe dos avisos por el mismo hecho
// aunque haya elegido varios tipos (CU-64). El campo estado del aviso lleva siempre el estado real del envío.
internal static class ClasificadorAvisos
{
    public const string EnDevolucion = "EnDevolucion";

    public static TipoEventoAviso Clasificar(string estadoEnvio, bool intentoFallido) =>
        estadoEnvio == EnDevolucion ? TipoEventoAviso.Devolucion
        : intentoFallido ? TipoEventoAviso.EntregaFallida
        : TipoEventoAviso.CambioDeEstado;
}

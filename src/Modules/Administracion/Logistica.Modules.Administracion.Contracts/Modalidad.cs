namespace Logistica.Modules.Administracion.Contracts;

// Modalidad de un envío (modelo de dominio; RF 3). La define Administración, porque el cuadro tarifario y los plazos
// dependen de ella, y Envíos la usa desde acá: un módulo sólo ve los Contracts de otro (guía, sección 2.8, regla 2).
public enum Modalidad
{
    Estandar,
    Urgente,
}

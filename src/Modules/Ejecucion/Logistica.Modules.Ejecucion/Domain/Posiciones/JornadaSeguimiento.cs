namespace Logistica.Modules.Ejecucion.Domain.Posiciones;

internal enum ValidezCaptura { Valida, RutaNoEnCurso, FueraDeJornada }

// Hechos consultados a los módulos dueños; las reglas de admisión del GPS pertenecen a Ejecución.
internal sealed record JornadaSeguimiento(bool EnCurso, bool Finalizada, DateTimeOffset? DespachadaEn,
    DateTimeOffset? RendidaEn)
{
    public ValidezCaptura Evaluar(DateTimeOffset captura)
    {
        if (!EnCurso && !(Finalizada && RendidaEn is not null)) return ValidezCaptura.RutaNoEnCurso;
        if (DespachadaEn is not DateTimeOffset inicio || captura < inicio
            || RendidaEn is DateTimeOffset fin && captura >= fin) return ValidezCaptura.FueraDeJornada;
        return ValidezCaptura.Valida;
    }
    public bool SoloHistorial => RendidaEn is not null || Finalizada;
}

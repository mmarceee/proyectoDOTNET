namespace Logistica.Modules.Administracion.Domain.Reglas;

// Los valores editables de una versión de reglas operativas (CU-06, paso 4), todos juntos: así se copian de la vigente al
// borrador y se modifican desde un solo formulario. Se validan al publicar (A1), no al editar: el borrador puede quedar
// a medio completar.
internal sealed record DatosReglas(
    int MaxIntentos,
    int PlazoEntreIntentosHoras,
    PruebaExigida PruebaExigida,
    PoliticaDevolucion PoliticaDevolucion,
    IReadOnlyList<PlazoModalidad> Plazos,
    int AnticipacionReprogramacionHoras,
    int MaxParadasPorRuta);

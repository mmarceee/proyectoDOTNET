namespace Logistica.Modules.Administracion.Contracts.Results;

// Las reglas operativas de una versión publicada (CU-06), para Envíos (CU-10, CU-17, CU-18), Planificación, la PWA y el
// seguimiento (CU-60, CU-61). El envío guarda VersionId al admitirse y conserva esas reglas (letra 6.6). La firma no
// figura porque es siempre obligatoria.
public sealed record ReglasOperativas(
    Guid VersionId, int Numero,
    int MaxIntentos, TimeSpan PlazoEntreIntentos,
    bool ExigeFoto, bool ExigeNombreYDocumento,
    bool DevolverTrasIntentosAgotados, int PlazoDevolucionDias,
    IReadOnlyList<PlazoModalidadResultado> Plazos,
    TimeSpan AnticipacionReprogramacion,
    int MaxParadasPorRuta,
    IReadOnlyList<MotivoNoEntregaResultado> Motivos);

// El plazo comprometido para una modalidad.
public sealed record PlazoModalidadResultado(Modalidad Modalidad, TimeSpan Plazo);

// Un motivo del catálogo de esa versión. El Id es el de esa versión: el envío lo busca en el catálogo de su versión.
public sealed record MotivoNoEntregaResultado(Guid Id, string Codigo, string Descripcion, bool Reprogramable,
    bool ExigeEvidencia);

namespace Logistica.Modules.Administracion.Contracts.Results;

public sealed record ReglasOperativas(
    Guid VersionId, int Numero,
    int MaxIntentos, TimeSpan PlazoEntreIntentos,
    bool ExigeFoto, bool ExigeNombreYDocumento,          // la firma siempre es obligatoria
    bool DevolverTrasIntentosAgotados, int PlazoDevolucionDias,
    IReadOnlyList<PlazoModalidadResultado> Plazos,        // Estándar y Urgente
    TimeSpan AnticipacionReprogramacion,                  // CU-61
    int MaxParadasPorRuta,
    IReadOnlyList<MotivoNoEntregaResultado> Motivos);

public sealed record PlazoModalidadResultado(Modalidad Modalidad, TimeSpan Plazo);

public sealed record MotivoNoEntregaResultado(Guid Id, string Codigo, string Descripcion,
    bool Reprogramable, bool ExigeEvidencia);

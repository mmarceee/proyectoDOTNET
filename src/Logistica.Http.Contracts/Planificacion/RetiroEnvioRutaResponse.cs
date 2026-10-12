namespace Logistica.Http.Contracts.Planificacion;

public sealed record RetiroEnvioRutaResponse(Guid Id, Guid EnvioId, string NumeroEnvio, int OrdenAnterior,
    int ParadasRestantes, long RevisionRuta, Guid? ResponsableId, DateTimeOffset OcurridoEn);

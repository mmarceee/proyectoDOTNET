namespace Logistica.Http.Contracts.Planificacion;

public sealed record ProponerOrdenParadasRequest(long RevisionEsperada);
public sealed record RevisarOrdenParadasRequest(long RevisionEsperada, IReadOnlyList<Guid> ParadaIds);
public sealed record GuardarOrdenParadasRequest(long RevisionEsperada, IReadOnlyList<Guid> ParadaIds,
    bool ConfirmarAdvertencias = false);
public sealed record OrdenParadasResponse(Guid RutaId, long Revision, IReadOnlyList<ParadaOrdenResponse> Paradas,
    IReadOnlyList<AdvertenciaOrdenResponse> Advertencias);
public sealed record ParadaOrdenResponse(Guid Id, int Orden, string Direccion, TimeOnly? Desde, TimeOnly? Hasta,
    string Zona, string CodigoPostal);
public sealed record AdvertenciaOrdenResponse(Guid ParadaId, string Mensaje);

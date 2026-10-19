namespace Logistica.Modules.Envios.Contracts;

public interface IEnviosHojaRuta
{
    Task<IReadOnlyList<EnvioParaHoja>> ConsultarAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
public sealed record EnvioParaHoja(Guid Id, string Numero, string Estado, string Nombre, string Telefono,
    string Calle, string NumeroPuerta, string Localidad, string Departamento, string CodigoPostal,
    string? Referencia, Guid? ZonaId, Guid? FranjaId, IReadOnlyList<BultoParaHoja> Bultos, ReglasParaHoja Reglas);
public sealed record BultoParaHoja(Guid Id, string Codigo);
public sealed record ReglasParaHoja(string Origen, Guid? VersionId, bool ExigeFoto, bool ExigeNombreYDocumento,
    int MaxIntentos, TimeSpan PlazoEntreIntentos, IReadOnlyList<MotivoParaHoja> Motivos);
public sealed record MotivoParaHoja(Guid Id, string Codigo, string Nombre, bool Reprogramable, bool ExigeEvidencia);

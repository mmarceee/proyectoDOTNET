namespace Logistica.Modules.Envios.Contracts;

public interface IEnviosHojaRuta
{
    // Datos y estados completos del envío. Ejecución selecciona el manifiesto de la ruta:
    // así conserva entregados de esta visita y excluye los de rutas anteriores.
    Task<IReadOnlyList<EnvioParaHoja>> ConsultarAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
public sealed record EnvioParaHoja(Guid Id, string Numero, string Estado, string Nombre, string Telefono,
    string Calle, string NumeroPuerta, string Localidad, string Departamento, string CodigoPostal,
    string? Referencia, Guid? ZonaId, Guid? FranjaId, IReadOnlyList<BultoParaHoja> Bultos, ReglasParaHoja Reglas);
public sealed record BultoParaHoja(Guid Id, string Codigo, string Estado = "");
public sealed record ReglasParaHoja(string Origen, Guid? VersionId, bool ExigeFoto, bool ExigeNombreYDocumento,
    int MaxIntentos, TimeSpan PlazoEntreIntentos, IReadOnlyList<MotivoParaHoja> Motivos,
    // RF 33: la PWA lo necesita para ofrecer, o no, entregar sólo parte de los bultos sin conexión (RF 32).
    bool AdmiteEntregaParcial = false);
public sealed record MotivoParaHoja(Guid Id, string Codigo, string Nombre, bool Reprogramable, bool ExigeEvidencia);

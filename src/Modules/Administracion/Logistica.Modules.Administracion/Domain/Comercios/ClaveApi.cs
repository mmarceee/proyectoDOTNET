using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Comercios;

internal enum TipoClaveApi
{
    Produccion,
    Prueba,
}

internal sealed class ClaveApi : Entity, IOperadorOwned, IComercioOwned
{
    public const int LargoMaximoNombre = 100;
    public const int LargoPrefijo = 16;

    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public Guid RelacionComercialId { get; private set; }
    public string Nombre { get; private set; } = "";
    public TipoClaveApi Tipo { get; private set; }
    public string Hash { get; private set; } = "";
    public string Prefijo { get; private set; } = "";
    public DateTimeOffset CreadaEn { get; private set; }
    public DateTimeOffset? UltimoUsoEn { get; private set; }
    public DateTimeOffset? RevocadaEn { get; private set; }

    private ClaveApi() { }

    public static ClaveApi Crear(RelacionComercial relacion, string nombre, TipoClaveApi tipo,
        string hash, string prefijo, DateTimeOffset ahora)
    {
        if (relacion.Estado != EstadoRelacion.Activa)
            throw new DomainException("La relación con el operador debe estar activa para crear claves.");
        nombre = nombre.Trim();
        if (nombre.Length is 0 or > LargoMaximoNombre)
            throw new DomainException($"El nombre debe tener entre 1 y {LargoMaximoNombre} caracteres.");

        return new ClaveApi
        {
            OperadorId = relacion.OperadorId,
            ComercioId = relacion.ComercioId,
            RelacionComercialId = relacion.Id,
            Nombre = nombre,
            Tipo = tipo,
            Hash = hash,
            Prefijo = prefijo,
            CreadaEn = ahora.ToUniversalTime(),
        };
    }

    public void Revocar(DateTimeOffset ahora) => RevocadaEn ??= ahora.ToUniversalTime();

    public void RegistrarUso(DateTimeOffset ahora)
    {
        if (UltimoUsoEn is null || UltimoUsoEn < ahora)
            UltimoUsoEn = ahora.ToUniversalTime();
    }
}

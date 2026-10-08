using Logistica.Modules.Envios.Domain.Envios;
using Logistica.SharedKernel;

namespace Logistica.Modules.Envios.Domain.Incidencias;

internal enum TipoIncidencia { Reclamo, Extravio, DañoEnBulto, Otro }
internal enum EstadoIncidencia { Abierta, EnProceso, Resuelta }

// Agregado independiente de Envio, según el modelo de dominio.
internal sealed class Incidencia : Entity, IOperadorOwned, IComercioOwned
{
    public Guid EnvioId { get; private set; }
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public TipoIncidencia Tipo { get; private set; }
    public string Descripcion { get; private set; } = "";
    public EstadoIncidencia Estado { get; private set; }
    public DateTimeOffset CreadaEn { get; private set; }
    public DateTimeOffset? ResueltaEn { get; private set; }
    public string? Resolucion { get; private set; }

    private Incidencia() { }

    public static Incidencia Crear(Envio envio, TipoIncidencia tipo, string descripcion, DateTimeOffset ahora)
    {
        if (!Enum.IsDefined(tipo) || string.IsNullOrWhiteSpace(descripcion))
        {
            throw new DomainException("La incidencia necesita tipo válido y descripción.");
        }

        return new Incidencia
        {
            EnvioId = envio.Id, OperadorId = envio.OperadorId, ComercioId = envio.ComercioId,
            Tipo = tipo, Descripcion = descripcion.Trim(), Estado = EstadoIncidencia.Abierta, CreadaEn = ahora,
        };
    }
}

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

    // CU-22, paso 4: el despachador la está atendiendo. Sólo una incidencia abierta pasa a en proceso.
    public void PasarAEnProceso()
    {
        if (Estado != EstadoIncidencia.Abierta)
        {
            throw new DomainException("Sólo una incidencia abierta puede pasar a en proceso.");
        }

        Estado = EstadoIncidencia.EnProceso;
    }

    // CU-22, paso 5: queda resuelta con la resolución y la fecha. Sin resolución no se puede (A2), y una incidencia
    // resuelta no se vuelve a resolver. Una de los faltantes de una rendición (CU-55) puede resolverse sin pasar por
    // en proceso.
    public void Resolver(string resolucion, DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(resolucion))
        {
            throw new DomainException("Indicá cómo se resolvió la incidencia.");
        }

        if (Estado == EstadoIncidencia.Resuelta)
        {
            throw new DomainException("La incidencia ya está resuelta.");
        }

        Estado = EstadoIncidencia.Resuelta;
        Resolucion = resolucion.Trim();
        ResueltaEn = ahora;
    }
}

using Logistica.SharedKernel;

namespace Logistica.Modules.Administracion.Domain.Comercios;

// "Este comercio trabaja con este operador": el vínculo muchos a muchos.
// Lleva los dos marcadores: el personal de un operador ve sólo sus relaciones y un comercio sólo las suyas (ADR-0002, sección 2.6).
internal sealed class RelacionComercial : Entity, IOperadorOwned, IComercioOwned
{
    public Guid OperadorId { get; private set; }
    public Guid ComercioId { get; private set; }
    public string EmailContacto { get; private set; } = "";
    public EstadoRelacion Estado { get; private set; }
    public DateTimeOffset FechaAlta { get; private set; }

    private RelacionComercial() { } // para EF Core

    // Suspender, reactivar y dar de baja llegan con CU-01.
    public static RelacionComercial Crear(Guid operadorId, Guid comercioId, string emailContacto, DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(emailContacto))
        {
            throw new DomainException("La relación comercial necesita un correo de contacto.");
        }

        return new RelacionComercial
        {
            OperadorId = operadorId,
            ComercioId = comercioId,
            EmailContacto = emailContacto,
            Estado = EstadoRelacion.Activa,
            FechaAlta = ahora,
        };
    }
}

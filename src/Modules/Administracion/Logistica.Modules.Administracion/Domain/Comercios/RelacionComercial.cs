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
    public uint Version { get; private set; }

    private RelacionComercial() { } // para EF Core

    public void ModificarContacto(string email)
    {
        if (Estado == EstadoRelacion.Baja) throw new DomainException("La relación está dada de baja.");
        EmailContacto = ValidarCorreo(email);
    }

    public void Suspender()
    {
        if (Estado != EstadoRelacion.Activa) throw new DomainException("Sólo se puede suspender una relación activa.");
        Estado = EstadoRelacion.Suspendida;
    }

    public void Reactivar()
    {
        if (Estado != EstadoRelacion.Suspendida) throw new DomainException("Sólo se puede reactivar una relación suspendida.");
        Estado = EstadoRelacion.Activa;
    }

    public void DarDeBaja(int enviosPendientes)
    {
        if (Estado == EstadoRelacion.Baja) throw new DomainException("La relación ya está dada de baja.");
        if (enviosPendientes != 0) throw new DomainException($"No se puede dar de baja: hay {enviosPendientes} envíos sin terminar.");
        Estado = EstadoRelacion.Baja;
    }

    private static string ValidarCorreo(string email)
    {
        email = email?.Trim() ?? "";
        if (!System.Net.Mail.MailAddress.TryCreate(email, out var direccion) || direccion.Address != email)
            throw new DomainException("El correo de contacto no es válido.");
        return email;
    }

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
            EmailContacto = ValidarCorreo(emailContacto),
            Estado = EstadoRelacion.Activa,
            FechaAlta = ahora,
        };
    }
}

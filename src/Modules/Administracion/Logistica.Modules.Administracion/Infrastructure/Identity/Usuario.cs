using Logistica.SharedKernel;
using Microsoft.AspNetCore.Identity;

namespace Logistica.Modules.Administracion.Infrastructure.Identity;

// Usuario del sistema sobre ASP.NET Core Identity (modelo de dominio, Usuario). El perfil es un rol de Identity (CU-02).
// No implementa IOperadorOwned: el inicio de sesión lo busca antes de conocer el inquilino (ADR-0002, sección 2.4).
// El personal tiene OperadorId; el usuario de comercio tiene ComercioId y elige el operador en cada sesión (CU-03).
internal sealed class Usuario : IdentityUser<Guid>
{
    public string Nombre { get; private set; } = "";
    public Guid? OperadorId { get; private set; }
    public Guid? ComercioId { get; private set; }
    public bool Activo { get; private set; }

    private Usuario() { } // para EF Core

    // Administrador, despachador, repartidor u operario de depósito: trabaja para un solo operador.
    public static Usuario CrearPersonal(Guid operadorId, string nombre, string email, Guid? id = null)
    {
        if (operadorId == Guid.Empty)
        {
            throw new DomainException("El personal necesita un operador.");
        }

        var usuario = Crear(nombre, email, id);
        usuario.OperadorId = operadorId;
        return usuario;
    }

    // Usuario de comercio: compartido por todos los operadores con los que trabaja su comercio (CU-02, decisión 4).
    public static Usuario CrearDeComercio(Guid comercioId, string nombre, string email, Guid? id = null)
    {
        if (comercioId == Guid.Empty)
        {
            throw new DomainException("El usuario de comercio necesita un comercio.");
        }

        var usuario = Crear(nombre, email, id);
        usuario.ComercioId = comercioId;
        return usuario;
    }

    private static Usuario Crear(string nombre, string email, Guid? id)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new DomainException("El usuario necesita un nombre.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("El usuario necesita un correo.");
        }

        // El correo es también el nombre de usuario con el que se inicia sesión.
        return new Usuario
        {
            Id = id ?? Guid.CreateVersion7(),
            Nombre = nombre.Trim(),
            Email = email.Trim(),
            UserName = email.Trim(),
            Activo = true,
        };
    }
}

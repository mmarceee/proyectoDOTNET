using Logistica.SharedKernel;
using Microsoft.AspNetCore.Identity;

namespace Logistica.Modules.Administracion.Infrastructure.Identity;

// Identity no lanza excepciones: devuelve un IdentityResult con errores en inglés. Acá se traducen y se convierten en
// DomainException, para que la página los muestre como cualquier otra regla rota (CU-02, A1 y A2).
internal static class ErroresDeIdentity
{
    public static void Verificar(IdentityResult resultado)
    {
        if (!resultado.Succeeded)
        {
            throw new DomainException(string.Join(" ", resultado.Errors.Select(Traducir).Distinct()));
        }
    }

    public static string Traducir(IdentityError error)
    {
        return error.Code switch
        {
            "DuplicateEmail" or "DuplicateUserName" => "Ya existe un usuario con ese correo.",
            "InvalidEmail" or "InvalidUserName" => "El correo no es válido.",
            "PasswordTooShort" => "La contraseña tiene que tener al menos 6 caracteres.",
            "PasswordRequiresNonAlphanumeric" => "La contraseña tiene que tener al menos un símbolo.",
            "PasswordRequiresDigit" => "La contraseña tiene que tener al menos un número.",
            "PasswordRequiresLower" => "La contraseña tiene que tener al menos una minúscula.",
            "PasswordRequiresUpper" => "La contraseña tiene que tener al menos una mayúscula.",
            _ => error.Description,
        };
    }
}

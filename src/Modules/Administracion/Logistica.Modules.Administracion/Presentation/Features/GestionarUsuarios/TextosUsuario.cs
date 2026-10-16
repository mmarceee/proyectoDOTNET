using Logistica.Modules.Administracion.Application.Features.GestionarUsuarios;
using Logistica.Modules.Administracion.Contracts;

namespace Logistica.Modules.Administracion.Presentation.Features.GestionarUsuarios;

// Cómo se muestran los perfiles y los estados en las páginas de CU-02.
internal static class TextosUsuario
{
    // Los perfiles del personal, para el alta y el cambio de perfil (A3).
    public static readonly Perfil[] PerfilesDelPersonal =
        [Perfil.Administrador, Perfil.Despachador, Perfil.OperarioDeposito, Perfil.Repartidor];

    public static string NombreDePerfil(Perfil perfil) => perfil switch
    {
        Perfil.Administrador => "Administrador",
        Perfil.Despachador => "Despachador",
        Perfil.Repartidor => "Repartidor",
        Perfil.OperarioDeposito => "Operario de depósito",
        Perfil.UsuarioComercio => "Usuario de comercio",
        _ => perfil.ToString(),
    };

    public static (string Texto, string Clase) Estado(EstadoUsuario estado) => estado switch
    {
        EstadoUsuario.Activo => ("Activo", "text-bg-success"),
        EstadoUsuario.PendienteDeActivacion => ("Pendiente de activación", "text-bg-warning"),
        _ => ("Desactivado", "text-bg-secondary"),
    };
}

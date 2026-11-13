using Logistica.Modules.Administracion.Contracts;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Logistica.Modules.Administracion.Infrastructure.Identity;

// Datos iniciales de Identity (ADR-0002, sección 2.8): los perfiles como roles, y usuarios de demostración para
// entrar como cada operador y como Tienda Demo. Corren al iniciar, después de DatosIniciales; son idempotentes.
// Usuario no tiene filtro de inquilino (sección 2.4): el operador o el comercio se asignan acá, explícitos.
internal sealed class UsuariosIniciales(
    UserManager<Usuario> usuarios, RoleManager<IdentityRole<Guid>> roles, VinculoRepartidor vinculos,
    IConfiguration configuracion, IHostEnvironment entorno)
{
    // Sólo para el desarrollo y las pruebas. Cumple la política de contraseñas por defecto de Identity.
    public const string ContrasenaDemo = "Demo-2026!";

    private readonly string _contrasena = ContrasenaInicial(configuracion, entorno);

    // El repositorio es público: en producción la contraseña de los usuarios de demostración sale de Demo:Contrasena, un
    // secreto que genera Terraform, y sin él la API no arranca. Fuera de producción, ContrasenaDemo si no se configura otra.
    // Sólo vale para los usuarios que todavía no existen: el seed no cambia contraseñas.
    internal static string ContrasenaInicial(IConfiguration configuracion, IHostEnvironment entorno)
    {
        if (configuracion["Demo:Contrasena"] is { Length: > 0 } contrasena)
        {
            return contrasena;
        }

        return entorno.IsProduction()
            ? throw new InvalidOperationException("En producción, configurá Demo:Contrasena (Demo__Contrasena) para los usuarios de demostración.")
            : ContrasenaDemo;
    }

    public async Task SembrarAsync()
    {
        foreach (var perfil in Enum.GetNames<Perfil>())
        {
            if (!await roles.RoleExistsAsync(perfil))
            {
                Verificar(await roles.CreateAsync(new IdentityRole<Guid>(perfil)), $"el perfil {perfil}");
            }
        }

        var demo = DatosIniciales.OperadorDemoId;
        var este = DatosIniciales.OperadorEsteId;

        await CrearAsync(Usuario.CrearPersonal(demo, "Administración Demo", "admin@operador-demo.uy"), Perfil.Administrador);
        await CrearAsync(Usuario.CrearPersonal(demo, "Despacho Demo", "despacho@operador-demo.uy"), Perfil.Despachador);
        await CrearAsync(Usuario.CrearPersonal(este, "Administración Este", "admin@envios-del-este.uy"), Perfil.Administrador);
        await CrearAsync(Usuario.CrearPersonal(este, "Despacho Este", "despacho@envios-del-este.uy"), Perfil.Despachador);
        var repartidorDemo = await CrearAsync(Usuario.CrearPersonal(demo, "Repartidor Demo", "repartidor@operador-demo.uy"), Perfil.Repartidor);
        var repartidorEste = await CrearAsync(Usuario.CrearPersonal(este, "Repartidor Este", "repartidor@envios-del-este.uy"), Perfil.Repartidor);

        // Cada usuario repartidor es el primer repartidor de su operador (ADR-0004, sección 2.3).
        await vinculos.VincularAsync(demo, DatosIniciales.RepartidorDemoId, repartidorDemo.Id, CancellationToken.None);
        await vinculos.VincularAsync(este, DatosIniciales.RepartidorEsteId, repartidorEste.Id, CancellationToken.None);
        await CrearAsync(Usuario.CrearPersonal(demo, "Depósito Demo", "deposito@operador-demo.uy"), Perfil.OperarioDeposito);
        await CrearAsync(Usuario.CrearPersonal(este, "Depósito Este", "deposito@envios-del-este.uy"), Perfil.OperarioDeposito);

        // Administra la plataforma: da de alta operadores y sus administradores. No pertenece a ningún operador.
        await CrearAsync(Usuario.CrearAdministradorGeneral("Administración General", "admin@plataforma.uy"),
            Perfil.AdministradorGeneral);

        // Trabaja con los dos operadores: elige uno en la sesión (CU-03).
        await CrearAsync(Usuario.CrearDeComercio(DatosIniciales.ComercioDemoId, "Tienda Demo", "comercio@tiendademo.uy"),
            Perfil.UsuarioComercio);
    }

    // Devuelve el usuario ya existente o el recién creado.
    private async Task<Usuario> CrearAsync(Usuario usuario, Perfil perfil)
    {
        if (await usuarios.FindByEmailAsync(usuario.Email!) is Usuario existente)
        {
            return existente;
        }

        Verificar(await usuarios.CreateAsync(usuario, _contrasena), usuario.Email!);
        Verificar(await usuarios.AddToRoleAsync(usuario, perfil.ToString()), usuario.Email!);
        return usuario;
    }

    // Identity no lanza excepciones: devuelve un resultado. Si el seed falla, la API no arranca a medias.
    private static void Verificar(IdentityResult resultado, string que)
    {
        if (!resultado.Succeeded)
        {
            throw new InvalidOperationException(
                $"No se pudo cargar {que}: {string.Join(" ", resultado.Errors.Select(e => e.Description))}");
        }
    }
}

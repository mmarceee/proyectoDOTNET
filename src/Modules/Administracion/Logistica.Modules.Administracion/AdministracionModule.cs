using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Administracion.Application;
using Logistica.Modules.Administracion.Application.Features.ConsultarRecursosPlanificacion;
using Logistica.Modules.Administracion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Logistica.Modules.Administracion.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Logistica.Modules.Administracion.Application.Features.IniciarSesion;
using Logistica.Modules.Administracion.Application.Features.SeleccionarOperador;
using Logistica.Modules.Administracion.Contracts;
using System.Security.Claims;
using Logistica.Modules.Administracion.Presentation.Autorizacion;
using Microsoft.AspNetCore.Authorization;

namespace Logistica.Modules.Administracion;

// Punto de entrada del módulo: lo único público que usa el host (ADR-0001, addendum 2).
public static class AdministracionModule
{
    public static IServiceCollection AddAdministracionModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AdministracionDbContext>(
            configuration.GetConnectionString("Postgres"), AdministracionDbContext.Schema);

        services.AddScoped<DatosIniciales>();
        services.AddScoped<UsuariosIniciales>();
        services.AddScoped<VinculoRepartidor>();

        // CU-07: identidad visual.
        services.AddScoped<Domain.Operadores.IOperadorRepository, OperadorRepository>();
        services.AddScoped<Application.Features.GestionarIdentidadVisual.ConsultarIdentidadVisualHandler>();
        services.AddScoped<Application.Features.GestionarIdentidadVisual.ActualizarIdentidadVisualHandler>();
        services.AddScoped<IServicioSesion, ServicioSesion>();

        // CU-02: gestionar usuarios.
        services.AddScoped<Application.Features.GestionarUsuarios.IServicioUsuarios, ServicioUsuarios>();
        services.AddScoped<Application.Features.GestionarUsuarios.IDatosParaUsuarios, DatosParaUsuarios>();
        services.AddScoped<Application.Features.GestionarUsuarios.ConsultarUsuariosHandler>();
        services.AddScoped<Application.Features.GestionarUsuarios.AltaUsuarioHandler>();
        services.AddScoped<Application.Features.GestionarUsuarios.ModificarUsuarioHandler>();
        services.AddScoped<Application.Features.SeleccionarOperador.IOperadoresDelComercio, OperadoresDelComercio>();
        services.AddScoped<IRecursosPlanificacionReader, RecursosPlanificacionReader>();
        services.AddScoped<Contracts.IAdministracionModuleApi, AdministracionModuleApi>();

        // Identity (CU-02): usuarios y perfiles en su propio DbContext, sobre la conexión de la unidad de trabajo: así el
        // alta de un usuario y su evento UsuarioCreado (outbox de Administración) se confirman juntos (ADR-0003).
        services.AddDbContext<IdentidadDbContext>((sp, options) => options.UseNpgsql(
            sp.GetRequiredService<UnidadDeTrabajo>().Conexion,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentidadDbContext.Schema)));

        services.AddIdentityCore<Usuario>(opciones =>
        {
            // Un correo, un usuario en todo el sistema (ADR-0002, sección 5).
            opciones.User.RequireUniqueEmail = true;
            opciones.Lockout.MaxFailedAccessAttempts = 5;
        })

            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IdentidadDbContext>()
            .AddSignInManager()
            .AddClaimsPrincipalFactory<FabricaDeClaims>()
            .AddDefaultTokenProviders();

        // CU-02, requerimiento 3: el enlace de invitación vence a las 48 horas. Es de un solo uso porque, al definir la
        // contraseña, cambia el security stamp y el token deja de valer.
        services.Configure<DataProtectionTokenProviderOptions>(opciones => opciones.TokenLifespan = TimeSpan.FromHours(48));

        // La sesión viaja en una cookie de Identity (propuesta de stack, sección 3.4; letra, sección 6.3).
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.Cookie.Name = "logistica.sesion";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            // Mismo origen para la API, el Backoffice y las apps WebAssembly (ADR-0001, addendum 5).
            cookie.Cookie.SameSite = SameSiteMode.Strict;
            cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
            cookie.SlidingExpiration = true;
            cookie.LoginPath = "/cuenta/ingresar";

            // A la API se le responde 401 o 403: un fetch del Portal no puede seguir una redirección a una página.
            cookie.Events.OnRedirectToLogin = contexto => ResponderORedirigir(contexto, StatusCodes.Status401Unauthorized);
            cookie.Events.OnRedirectToAccessDenied = contexto => ResponderORedirigir(contexto, StatusCodes.Status403Forbidden);
        });

        // Cada 30 minutos Identity rearma la cookie desde la base (validación del security stamp). El operador que eligió
        // el comercio no está en la base (ADR-0002, sección 2.6): se copia de la cookie anterior (CU-03).
        // Autorización por perfil (letra, sección 6.3): cada módulo usa estas políticas en sus endpoints.
        services.AddSingleton<IAuthorizationHandler, VerificadorDePerfil>();
        services.AddAuthorization(opciones =>
        {
            opciones.AddPolicy(Politicas.Comercio, p => p.AddRequirements(new PoliticaDePerfil(Perfil.UsuarioComercio, ExigeOperador: true)));
            opciones.AddPolicy(Politicas.OperarioDeposito, p => p.AddRequirements(new PoliticaDePerfil(Perfil.OperarioDeposito)));
            opciones.AddPolicy(Politicas.Administrador, p => p.AddRequirements(new PoliticaDePerfil(Perfil.Administrador)));
            opciones.AddPolicy(Politicas.Repartidor, p => p.AddRequirements(
                new PoliticaDePerfil(Perfil.Repartidor, ExigeOperador: true, ExigeRepartidor: true)));
        });

        services.AddHttpContextAccessor();
        services.AddOptions<SecurityStampValidatorOptions>().Configure<IHttpContextAccessor>((opciones, accesor) =>
        {
            opciones.OnRefreshingPrincipal = contexto => ConservarOperadorAsync(contexto, accesor);

            // CU-02, A4: desactivar o cambiar el perfil cambia el security stamp; las sesiones abiertas se enteran en la
            // próxima validación. Cada minuto, en lugar de los 30 por defecto, a cambio de una consulta por minuto y sesión.
            opciones.ValidationInterval = TimeSpan.FromMinutes(1);
        });


        return services;
    }

    public static IEndpointRouteBuilder MapAdministracionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/administracion").WithTags("Administracion");

        // CU-07: la marca del operador de la sesión, para los layouts.
        Presentation.Features.GestionarIdentidadVisual.MarcaDelOperadorEndpoint.Map(endpoints);
        return endpoints;
    }

    // Carga los datos iniciales. La llama el host al iniciar, después de las migraciones (ADR-0002, sección 2.8).
    public static async Task SembrarDatosInicialesAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<DatosIniciales>().SembrarAsync(ct);
        // Después de los operadores y el comercio: los usuarios los referencian.
        await scope.ServiceProvider.GetRequiredService<UsuariosIniciales>().SembrarAsync();
    }


    // Crea o actualiza las tablas de Identity. La llama el host junto con las migraciones de los módulos,
    // porque IdentidadDbContext no es un ModuleDbContext y MigrateModuleDatabasesAsync no lo ve.
    public static async Task MigrarIdentidadAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IdentidadDbContext>().Database.MigrateAsync(ct);
    }


    private static Task ResponderORedirigir(RedirectContext<CookieAuthenticationOptions> contexto, int estado)
    {
        // A la API, y a cualquier página cuando el perfil no alcanza (no hay página de acceso denegado): 401 o 403.
        if (contexto.Request.Path.StartsWithSegments("/api") || estado == StatusCodes.Status403Forbidden)
        {
            contexto.Response.StatusCode = estado;
        }
        else if (estado == StatusCodes.Status401Unauthorized && contexto.Request.Path.StartsWithSegments("/cuenta/operador"))
        {
            // Elegir operador es del usuario de comercio: sin sesión, va al ingreso del Portal.
            contexto.Response.Redirect("/portal/ingresar");
        }
        else
        {
            contexto.Response.Redirect(contexto.RedirectUri);
        }

        return Task.CompletedTask;
    }

    // Al rearmar la cookie, conserva el operador elegido sólo si la relación con el comercio sigue activa. Si se
    // suspendió, la sesión queda sin operador y el comercio tiene que volver a elegir (CU-03, A4).
    private static async Task ConservarOperadorAsync(SecurityStampRefreshingPrincipalContext contexto, IHttpContextAccessor accesor)
    {
        // El personal ya trae su operador desde FabricaDeClaims: sólo se copia para el usuario de comercio.
        if (contexto.NewPrincipal?.Identity is not ClaimsIdentity nueva
            || !Guid.TryParse(nueva.FindFirst(ClaimsDeSesion.ComercioId)?.Value, out var comercioId)
            || !Guid.TryParse(contexto.CurrentPrincipal?.FindFirst(ClaimsDeSesion.OperadorId)?.Value, out var operadorId))
        {
            return;
        }

        // El contexto de Identity no trae el request: la consulta se pide a los servicios del request actual.
        var http = accesor.HttpContext!;
        var activos = await http.RequestServices.GetRequiredService<IOperadoresDelComercio>()
            .ActivosAsync(comercioId, http.RequestAborted);

        if (activos.Any(o => o.Id == operadorId))
        {
            nueva.AddClaim(new Claim(ClaimsDeSesion.OperadorId, operadorId.ToString()));
        }
    }
}

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

namespace Logistica.Modules.Administracion;

// Punto de entrada del módulo: lo único público que usa el host (ADR-0001, addendum 2).
public static class AdministracionModule
{
    public static IServiceCollection AddAdministracionModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AdministracionDbContext>(
            configuration.GetConnectionString("Postgres"), AdministracionDbContext.Schema);

        services.AddScoped<DatosIniciales>();
        services.AddScoped<IServicioSesion, ServicioSesion>();
        services.AddScoped<IRecursosPlanificacionReader, RecursosPlanificacionReader>();
        services.AddScoped<Contracts.IAdministracionModuleApi, AdministracionModuleApi>();

        // Identity (CU-02): usuarios y perfiles en su propio DbContext. No usa la unidad de trabajo de los módulos.
        services.AddDbContext<IdentidadDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("Postgres"),
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
            .AddClaimsPrincipalFactory<FabricaDeClaims>();

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


        return services;
    }

    public static IEndpointRouteBuilder MapAdministracionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/administracion").WithTags("Administracion");

        // Mapear acá los endpoints de Presentation/Features, por ejemplo: CrearEnvioEndpoint.Map(group);
        return endpoints;
    }

    // Carga los datos iniciales. La llama el host al iniciar, después de las migraciones (ADR-0002, sección 2.8).
    public static async Task SembrarDatosInicialesAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<DatosIniciales>().SembrarAsync(ct);
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
        if (contexto.Request.Path.StartsWithSegments("/api"))
        {
            contexto.Response.StatusCode = estado;
        }
        else
        {
            contexto.Response.Redirect(contexto.RedirectUri);
        }

        return Task.CompletedTask;
    }

}

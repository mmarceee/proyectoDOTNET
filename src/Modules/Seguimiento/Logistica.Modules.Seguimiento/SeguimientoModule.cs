using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.BuildingBlocks.Infrastructure.Messaging;
using Logistica.Modules.Planificacion.Contracts.Events;
using Logistica.Modules.Seguimiento.Application;
using Logistica.Modules.Seguimiento.Application.Abstractions;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarSeguimiento;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarAvisos;
using Logistica.Modules.Seguimiento.Infrastructure.Messaging;
using Logistica.Modules.Seguimiento.Infrastructure.Persistence;
using Logistica.Modules.Seguimiento.Domain.Avisos;
using Logistica.Modules.Seguimiento.Application.Features.ConfigurarSuscripcionesAviso;
using Logistica.Modules.Seguimiento.Infrastructure.Security;
using Logistica.Modules.Seguimiento.Infrastructure.Http;
using Logistica.Modules.Seguimiento.Application.Features.EntregarAvisos;
using Logistica.Modules.Seguimiento.Application.Features.NotificarDestinatario;
using Logistica.Modules.Seguimiento.Application.HechosEnvio;
using Logistica.Modules.Seguimiento.Domain.Notificaciones;
using Logistica.Modules.Seguimiento.Infrastructure.Correo;
using Logistica.Modules.Administracion.Contracts.Events;
using Logistica.Modules.Seguimiento.Application.Features.HistorialAvisos;
using Logistica.Modules.Seguimiento.Contracts.Events;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Logistica.Modules.Seguimiento;

// Punto de entrada del módulo: lo único público que usa el host (ADR-0001, addendum 2).
public static class SeguimientoModule
{
    public static IServiceCollection AddSeguimientoModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<SeguimientoDbContext>(configuration.GetConnectionString("Postgres"), SeguimientoDbContext.Schema);
        services.AddScoped<IAvisosRepository, AvisosRepository>();
        services.AddScoped<ISuscripcionesAvisoRepository, SuscripcionesAvisoRepository>();
        services.AddScoped<ISuscripcionesAvisoReader, SuscripcionesAvisoReader>();
        services.AddScoped<ConsultarSuscripcionesAvisoHandler>();
        services.AddScoped<DesactivarSuscripcionAvisoHandler>();
        services.AddScoped<IHistorialAvisosReader, HistorialAvisosReader>();
        services.AddScoped<IReenviosAvisoRepository, ReenviosAvisoRepository>();
        services.AddScoped<ReenviarAvisosHandler>();
        services.AddScoped(sp => new ConsultarHistorialAvisosHandler(
            sp.GetRequiredService<IHistorialAvisosReader>(),
            sp.GetRequiredService<Administracion.Contracts.IAdministracionModuleApi>(),
            sp.GetRequiredService<SharedKernel.ICurrentTenant>()));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<EnviarPruebaAvisoHandler>();
        services.AddHttpClient<IEmisorAvisos, EmisorAvisosHttp>(http => http.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(ConectorAvisosHttp.CrearHandler);
        services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
        // Se valida al cifrar/descifrar, para que los otros casos de Seguimiento funcionen sin esta clave.
        services.AddSingleton<ISecretosAviso>(_ => new SecretosAviso(configuration["Seguimiento:Avisos:ClaveCifrado"]));
        services.AddScoped<RegenerarSecretoAvisoHandler>();
        // El Worker usa el mismo módulo pero no necesita Administración para entregar avisos.
        services.AddScoped(sp => new CrearSuscripcionAvisoHandler(
            sp.GetRequiredService<ISuscripcionesAvisoRepository>(),
            sp.GetRequiredService<Administracion.Contracts.IAdministracionModuleApi>(),
            sp.GetRequiredService<ISecretosAviso>(), sp.GetRequiredService<SharedKernel.ICurrentTenant>(),
            sp.GetRequiredService<SharedKernel.IUnidadDeTrabajo>()));
        services.AddScoped<ConsultarAvisosHandler>();

        // CU-64: una cola por evento de Envíos prepara las entregas; otra envía cada entrega y la reintenta sola.
        services.AddScoped<IEnviosSeguidosRepository, EnviosSeguidosRepository>();
        services.AddScoped<EnviosSeguidos>();
        services.AddScoped<IEntregasAvisoRepository, EntregasAvisoRepository>();
        services.AddScoped<IIntegrationEventPublisher, IntegrationEventPublisher>();
        services.AddScoped<PrepararAvisosHandler>();
        services.AddScoped<EntregarAvisoHandler>();
        foreach (var tipo in PrepararAvisos.Tipos)
        {
            services.AddKeyedScoped<IConsumidorIntegracion, PrepararAvisos>(PrepararAvisos.Cola(tipo));
            services.AddSingleton(new SuscripcionIntegracion(PrepararAvisos.Cola(tipo), tipo));
        }
        services.AddKeyedScoped<IConsumidorIntegracion, EntregarAviso>(EntregarAviso.Cola);
        services.AddSingleton(new SuscripcionIntegracion(EntregarAviso.Cola, AvisoPendiente.Tipo));

        // CU-62: correo al destinatario con la marca del operador. La dirección pública, el servidor SMTP y la clave
        // del token se validan al usarlos, así la API y el Worker arrancan aunque falten.
        services.AddScoped<INotificacionesRepository, NotificacionesRepository>();
        services.AddScoped<IIdentidadesOperadorRepository, IdentidadesOperadorRepository>();
        services.AddSingleton(_ => new DireccionPublica(configuration["Aplicacion:UrlPublica"]));
        services.AddSingleton(_ => new ServidorSmtp(configuration.GetConnectionString("Smtp"),
            configuration["Seguimiento:Correo:Remitente"]));
        services.AddScoped<IEmisorCorreo, EmisorCorreoSmtp>();
        services.AddScoped<NotificarDestinatarioHandler>();
        services.AddScoped<ActualizarIdentidadOperadorHandler>();
        foreach (var tipo in NotificarDestinatario.Tipos)
        {
            services.AddKeyedScoped<IConsumidorIntegracion, NotificarDestinatario>(NotificarDestinatario.Cola(tipo));
            services.AddSingleton(new SuscripcionIntegracion(NotificarDestinatario.Cola(tipo), tipo));
        }
        services.AddKeyedScoped<IConsumidorIntegracion, ActualizarIdentidadOperador>(ActualizarIdentidadOperador.Cola);
        services.AddSingleton(new SuscripcionIntegracion(ActualizarIdentidadOperador.Cola, IdentidadVisualActualizada.Tipo));

        services.AddKeyedScoped<IConsumidorIntegracion, NotificarRutaDespachada>(NotificarRutaDespachada.Cola);
        services.AddSingleton(new SuscripcionIntegracion(NotificarRutaDespachada.Cola, RutaDespachada.Tipo));

        // CU-60: la clave se lee recién cuando se usa, así la API arranca aunque falte y el error dice qué configurar.
        services.AddSingleton(_ => new ClaveSeguimiento(configuration["Seguimiento:ClaveToken"]));
        services.AddSingleton<TokenSeguimiento>();
        services.AddScoped<Contracts.ISeguimientoModuleApi, SeguimientoModuleApi>();

        // El handler necesita los contratos de Envíos y Administración, que sólo están en la API. Con una fábrica, el
        // Worker (que también registra este módulo) no intenta validarlo al arrancar.
        services.AddScoped(sp => new ConsultarSeguimientoHandler(
            sp.GetRequiredService<Envios.Contracts.IEnviosModuleApi>(),
            sp.GetRequiredService<Administracion.Contracts.IAdministracionModuleApi>(),
            sp.GetRequiredService<SharedKernel.ICacheDistribuida>()));

        // CU-60: cada cambio de estado de un envío borra su seguimiento de la caché; una cola por tipo de evento.
        foreach (var tipo in InvalidarSeguimiento.Tipos)
        {
            services.AddKeyedScoped<IConsumidorIntegracion, InvalidarSeguimiento>(InvalidarSeguimiento.Cola(tipo));
            services.AddSingleton(new SuscripcionIntegracion(InvalidarSeguimiento.Cola(tipo), tipo));
        }

        return services;
    }

    public static IEndpointRouteBuilder MapSeguimientoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        Presentation.Features.ConsultarAvisos.ConsultarAvisosEndpoint.Map(endpoints);
        Presentation.Features.ConfigurarSuscripcionesAviso.ConfigurarSuscripcionesAvisoEndpoints.Map(endpoints);
        Presentation.Features.HistorialAvisos.HistorialAvisosEndpoints.Map(endpoints);
        Presentation.Features.ConsultarSeguimiento.ConsultarSeguimientoEndpoint.Map(endpoints);
        return endpoints;
    }
}

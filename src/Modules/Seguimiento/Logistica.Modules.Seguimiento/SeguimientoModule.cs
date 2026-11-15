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
using Logistica.Modules.Seguimiento.Application.Features.EnviarInvitacion;
using Logistica.Modules.Seguimiento.Application.HechosEnvio;
using Logistica.Modules.Seguimiento.Domain.Notificaciones;
using Logistica.Modules.Seguimiento.Infrastructure.Correo;
using Logistica.Modules.Administracion.Contracts.Events;
using Logistica.Modules.Seguimiento.Application.Features.HistorialAvisos;
using Logistica.Modules.Seguimiento.Contracts.Events;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Logistica.Modules.Seguimiento.Application.Features.VerTablero;
using Logistica.Modules.Ejecucion.Contracts.Events;
using Logistica.Modules.Seguimiento.Presentation.Features.VerTablero;
using Logistica.Modules.Seguimiento.Application.Features.RecalcularIndicadores;
using Logistica.Modules.Seguimiento.Application.Features.ConsultarReportes;
using Logistica.Modules.Seguimiento.Domain.Indicadores;
using StackExchange.Redis;

namespace Logistica.Modules.Seguimiento;

// Punto de entrada del módulo: lo único público que usa el host (ADR-0001, addendum 2).
public static class SeguimientoModule
{
    // Medidor de los avisos fallidos (MetricasAvisos), que el Worker registra en OpenTelemetry.
    public const string Medidor = "Logistica.Seguimiento";

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
        // CU-66: como ConsultarSeguimientoHandler, usa contratos que sólo están en la API; con una fábrica, el Worker
        // (que también registra este módulo) no intenta validarlo al arrancar.
        services.AddScoped(sp => new ConsultarTableroHandler(
            sp.GetRequiredService<Planificacion.Contracts.IPlanificacionModuleApi>(),
            sp.GetRequiredService<Envios.Contracts.IEnviosModuleApi>(),
            sp.GetRequiredService<Ejecucion.Contracts.IEjecucionModuleApi>(),
            sp.GetRequiredService<Administracion.Contracts.IAdministracionModuleApi>(),
            sp.GetRequiredService<TimeProvider>()));

        // CU-66: SignalR y, si hay Valkey, su backplane: el Worker y la API publican en las mismas salas (decisión G1).
        var signalR = services.AddSignalR();
        var valkey = configuration.GetConnectionString("Valkey");
        if (!string.IsNullOrWhiteSpace(valkey))
        {
            signalR.AddStackExchangeRedis(valkey, opciones =>
            {
                opciones.Configuration.AbortOnConnectFail = false; // arranca aunque Valkey no esté, como la caché
                // Con Valkey caído, cada aviso falla enseguida en lugar de esperar 5 s en la cola del cliente: la
                // sincronización de la PWA, que avisa cada posición, no se demora (pruebas de resiliencia, 7.4).
                opciones.Configuration.BacklogPolicy = BacklogPolicy.FailFast;
                opciones.Configuration.AsyncTimeout = 1000;
                opciones.Configuration.ChannelPrefix = RedisChannel.Literal("logistica-tablero");
            });
        }
        services.AddScoped<ITableroEnVivo, TableroSignalR>();
        services.AddScoped<IConsumidorPosicionReportada, ActualizarPosicionTablero>();
        // Con fábrica por el mismo motivo que ConsultarTableroHandler: el contrato de Ejecución sólo está en la API.
        services.AddScoped(sp => new RevisarConflictoHandler(sp.GetRequiredService<Ejecucion.Contracts.IEjecucionModuleApi>()));

        // CU-66, paso 3: una cola por hecho que el Worker manda al tablero por el backplane.
        foreach (var tipo in NotificarNovedadTablero.Tipos)
        {
            services.AddKeyedScoped<IConsumidorIntegracion, NotificarNovedadTablero>(NotificarNovedadTablero.Cola(tipo));
            services.AddSingleton(new SuscripcionIntegracion(NotificarNovedadTablero.Cola(tipo), tipo));
        }

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

        // CU-71: la tabla de lectura de los indicadores de cumplimiento; una cola por evento de Envíos.
        services.AddScoped<IRegistrosCumplimientoRepository, RegistrosCumplimientoRepository>();
        services.AddScoped<RegistrarCumplimientoHandler>();
        foreach (var tipo in RegistrarCumplimiento.Tipos)
        {
            services.AddKeyedScoped<IConsumidorIntegracion, RegistrarCumplimiento>(RegistrarCumplimiento.Cola(tipo));
            services.AddSingleton(new SuscripcionIntegracion(RegistrarCumplimiento.Cola(tipo), tipo));
        }

        // CU-72: con fábrica, como ConsultarTableroHandler, porque los contratos de Administración y Depósito sólo están
        // en la API.
        services.AddScoped<IReportesReader, ReportesReader>();
        services.AddScoped(sp => new ConsultarReportesHandler(
            sp.GetRequiredService<IReportesReader>(),
            sp.GetRequiredService<Administracion.Contracts.IAdministracionModuleApi>(),
            sp.GetRequiredService<Deposito.Contracts.IDepositoModuleApi>(),
            sp.GetRequiredService<TimeProvider>()));

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

        // CU-02, paso 7: el correo de invitación con el enlace para definir la contraseña.
        services.AddScoped<EnviarInvitacionHandler>();
        services.AddKeyedScoped<IConsumidorIntegracion, EnviarInvitacion>(EnviarInvitacion.Cola);
        services.AddSingleton(new SuscripcionIntegracion(EnviarInvitacion.Cola, UsuarioInvitado.Tipo));

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
        // CU-61: con fábrica por el mismo motivo (el contrato de Envíos sólo está en la API). La verificación por correo
        // o teléfono se guarda en Seguimiento (requerimiento especial 4).
        services.AddScoped<Domain.Reprogramaciones.IVerificacionesReprogramacionRepository, VerificacionesReprogramacionRepository>();
        services.AddSingleton<Application.Features.SolicitarReprogramacion.SecretoVerificacion>();
        services.AddScoped(sp => new Application.Features.SolicitarReprogramacion.SolicitarReprogramacionHandler(
            sp.GetRequiredService<Envios.Contracts.IEnviosModuleApi>(),
            sp.GetRequiredService<SharedKernel.ICacheDistribuida>(),
            sp.GetRequiredService<Domain.Reprogramaciones.IVerificacionesReprogramacionRepository>(),
            sp.GetRequiredService<Application.Features.SolicitarReprogramacion.SecretoVerificacion>(),
            sp.GetRequiredService<IEmisorCorreo>(),
            sp.GetRequiredService<IIdentidadesOperadorRepository>(),
            sp.GetRequiredService<TokenSeguimiento>(),
            sp.GetRequiredService<DireccionPublica>(),
            sp.GetRequiredService<SharedKernel.ICurrentTenant>(),
            sp.GetRequiredService<TimeProvider>()));

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
        Presentation.Features.SolicitarReprogramacion.SolicitarReprogramacionEndpoint.Map(endpoints);
        Presentation.Features.VerTablero.ConsultarTableroEndpoint.Map(endpoints);
        endpoints.MapHub<TableroHub>(TableroHub.Ruta);
        return endpoints;
    }
}

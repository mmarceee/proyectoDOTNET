using Logistica.BuildingBlocks.Infrastructure.Persistence;
using Logistica.Modules.Envios.Application;
using Logistica.Modules.Envios.Application.Features.CrearEnvio;
using Logistica.Modules.Envios.Contracts;
using Logistica.Modules.Envios.Domain.Envios;
using Logistica.Modules.Envios.Infrastructure.Persistence;
using Logistica.Modules.Envios.Presentation.Features.CrearEnvio;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Logistica.Modules.Envios.Application.Features.ConsultarEnvios;
using Logistica.Modules.Envios.Application.Features.ConsultarEnviosApi;
using Logistica.Modules.Envios.Presentation.Features.ConsultarEnviosApi;
using Logistica.Modules.Envios.Application.Features.ConsultarDetalleEnvio;
using Logistica.Modules.Envios.Presentation.Features.ConsultarDetalleEnvio;
using Logistica.Modules.Envios.Application.Features.CancelarEnvio;
using Logistica.Modules.Envios.Presentation.Features.CancelarEnvio;
using Logistica.Modules.Envios.Application.Features.ReprogramarEnvio;
using Logistica.Modules.Envios.Presentation.Features.ReprogramarEnvio;
using Logistica.Modules.Envios.Application.Features.DeclararExtravio;
using Logistica.Modules.Envios.Application.Features.IniciarDevolucion;
using Logistica.Modules.Envios.Presentation.Features.IniciarDevolucion;

namespace Logistica.Modules.Envios;

// Punto de entrada del módulo: lo único público que usa el host (ADR-0001, addendum 2).
public static class EnviosModule
{
    public static IServiceCollection AddEnviosModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<EnviosDbContext>(configuration.GetConnectionString("Postgres"), EnviosDbContext.Schema);
        services.AddScoped<IEnvioRepository, EnvioRepository>();
        services.AddScoped<IEnviosHojaRuta, EnviosHojaRuta>();
        services.AddScoped<Contracts.IEnviosDeComercio, EnviosDeComercio>();

        services.AddScoped<IEnviosListadoReader, EnviosListadoReader>();
        services.AddScoped<ConsultarEnviosHandler>();
        services.AddScoped<IEnviosApiReader, EnviosApiReader>();
        services.AddScoped<ConsultarEnviosApiHandler>();
        services.AddScoped<IEnvioDetalleReader, EnvioDetalleReader>();
        // Uso educativo del laboratorio: licencia Community de QuestPDF (decisión CU-15).
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        services.AddScoped<Application.Features.ImprimirEtiquetas.IEtiquetasReader, EtiquetasReader>();
        services.AddScoped<Application.Features.ImprimirEtiquetas.IGeneradorEtiquetas, Infrastructure.Etiquetas.GeneradorEtiquetasPdf>();
        services.AddScoped<Application.Features.ImprimirEtiquetas.ImprimirEtiquetasHandler>();
        services.AddScoped<ConsultarDetalleEnvioHandler>();
        services.AddScoped<IArchivoEvidenciaReader, ArchivoEvidenciaReader>();
        services.AddScoped<ConsultarEvidenciaHandler>();
        services.AddScoped<CrearEnvioHandler>();
        services.AddScoped<Application.Features.ImportarEnvios.ImportarEnviosHandler>();
        services.AddScoped<Application.Features.ImportarEnvios.IAmbitoPorEnvio, Infrastructure.AmbitoPorEnvio>();
        services.AddScoped<CancelarEnvioHandler>();
        services.AddScoped<ReprogramarEnvioHandler>();
        services.AddScoped<DeclararExtravioHandler>();
        services.AddScoped<IniciarDevolucionHandler>();
        services.AddScoped<DevolverReprogramadosVencidosHandler>();
        services.AddScoped<Application.Features.ReprogramarPorDestinatario.ReprogramarPorDestinatarioHandler>();

        // Las reglas de entrega y el catálogo de motivos de la versión del envío, pedidos a Administración (CU-06).
        services.AddScoped<IReglasDeEntrega, Infrastructure.ReglasDeEntregaDeAdministracion>();
        services.AddScoped<IIntegrationEventPublisher, IntegrationEventPublisher>();

        // Contrato para los otros módulos (ADR-0001, addendum 3).
        services.AddScoped<IEnviosModuleApi, EnviosModuleApi>();

        return services;
    }

    public static IEndpointRouteBuilder MapEnviosEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/envios").WithTags("Envios");

        CrearEnvioEndpoint.Map(group);
        Presentation.Features.ImprimirEtiquetas.ImprimirEtiquetasEndpoints.Map(group);
        Presentation.Features.ConsultarEnvios.EnviosPortalEndpoints.Map(group);
        Presentation.Features.ImportarEnvios.ImportarEnviosEndpoint.Map(group);
        CancelarEnvioEndpoint.Map(group);
        ReprogramarEnvioEndpoint.Map(group);
        IniciarDevolucionEndpoint.Map(group);
        EvidenciaEndpoint.Map(endpoints);

        return endpoints;
    }

    public static void MapEnviosApiPublica(this RouteGroupBuilder version, bool esConsola = false)
    {
        var sufijo = esConsola ? "ConsolaV1" : "V1";
        CrearEnvioApiEndpoint.Map(version, sufijo);
        ConsultarEnviosApiEndpoints.Map(version, sufijo);
        if (!esConsola)
            CrearEnvioApiEndpoint.MapSeguimientoPrueba(version);
    }
}

using System.Reflection;
using System.Text.RegularExpressions;
using Architecture = ArchUnitNET.Domain.Architecture;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Logistica.ArchitectureTests;

// Arquitectura cargada una sola vez para todas las pruebas.
// Los proyectos Contracts no se cargan: son la única parte de un módulo
// que otros módulos pueden usar, así que no forman parte de las reglas de aislamiento.
internal static class LogisticaArchitecture
{
    public static readonly string[] AllModules =
        ["Administracion", "Envios", "Planificacion", "Ejecucion", "Seguimiento", "Deposito"];

    public static readonly Assembly SharedKernelAssembly = Assembly.Load("Logistica.SharedKernel");
    public static readonly Assembly BuildingBlocksInfrastructureAssembly = Assembly.Load("Logistica.BuildingBlocks.Infrastructure");
    public static readonly Assembly BackofficeAssembly = Assembly.Load("Logistica.Backoffice");
    public static readonly Assembly HttpContractsAssembly = Assembly.Load("Logistica.Http.Contracts");
    public static readonly Assembly WorkerAssembly = Assembly.Load("Logistica.Worker");

    public static readonly Assembly[] WebAssemblyApps =
    [
        Assembly.Load("Logistica.PortalComercio"),
        Assembly.Load("Logistica.SeguimientoPublico"),
        Assembly.Load("Logistica.Repartidor.Pwa"),
    ];

    public static Assembly ModuleAssembly(string module) => Assembly.Load($"Logistica.Modules.{module}");

    // Todo lo de otro módulo salvo su proyecto Contracts, que es lo único que puede usarse.
    public static string[] InternalsOfOtherModules(string module) =>
        AllModules
            .Where(other => other != module)
            .SelectMany(other => new[]
            {
                $"Logistica.Modules.{other}.Domain",
                $"Logistica.Modules.{other}.Application",
                $"Logistica.Modules.{other}.Infrastructure",
                $"Logistica.Modules.{other}.Presentation",
            })
            .ToArray();

    public static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(
        [
            .. AllModules.Select(ModuleAssembly),
            SharedKernelAssembly,
            BuildingBlocksInfrastructureAssembly,
            BackofficeAssembly,
            HttpContractsAssembly,
            WorkerAssembly,
            .. WebAssemblyApps,
        ])
        .Build();

    // Tipos del namespace indicado o de cualquiera de sus sub-namespaces, incluidos los de paquetes externos.
    public static GivenTypesConjunction InNamespace(string root) =>
        Types(true).That().ResideInNamespaceMatching($@"^{Regex.Escape(root)}(\..+)?$");

    public static GivenTypesConjunction InAnyNamespace(params string[] roots) =>
        Types(true).That().ResideInNamespaceMatching(
            $@"^({string.Join("|", roots.Select(Regex.Escape))})(\..+)?$");
}

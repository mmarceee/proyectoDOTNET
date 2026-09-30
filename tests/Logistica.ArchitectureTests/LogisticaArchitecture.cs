using System.Reflection;
using System.Text.RegularExpressions;
using Architecture = ArchUnitNET.Domain.Architecture;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Logistica.ArchitectureTests;

// Arquitectura cargada una sola vez para todas las pruebas.
// Los proyectos Contracts no se cargan a propósito: son la única parte de un módulo
// que otros módulos pueden usar, así que no forman parte de las reglas de aislamiento.
internal static class LogisticaArchitecture
{
    public static readonly string[] AllModules =
        ["Administracion", "Envios", "Planificacion", "Ejecucion", "Seguimiento", "Deposito"];

    public static readonly Assembly SharedKernel = Assembly.Load("Logistica.SharedKernel");
    public static readonly Assembly BuildingBlocksInfrastructure = Assembly.Load("Logistica.BuildingBlocks.Infrastructure");

    public static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(
        [
            .. AllModules.Select(module => Assembly.Load($"Logistica.Modules.{module}")),
            SharedKernel,
            BuildingBlocksInfrastructure,
        ])
        .Build();

    // Tipos del namespace indicado o de cualquiera de sus sub-namespaces, incluidos los de paquetes externos.
    public static GivenTypesConjunction InNamespace(string root) =>
        Types(true).That().ResideInNamespaceMatching($@"^{Regex.Escape(root)}(\..+)?$");

    public static GivenTypesConjunction InAnyNamespace(params string[] roots) =>
        Types(true).That().ResideInNamespaceMatching(
            $@"^({string.Join("|", roots.Select(Regex.Escape))})(\..+)?$");
}

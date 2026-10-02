using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static Logistica.ArchitectureTests.LogisticaArchitecture;

namespace Logistica.ArchitectureTests;

// Reglas del lado cliente: DTOs HTTP y aplicaciones Blazor WebAssembly (ADR-0001, addendum 5).
public class ClientDependencyTests
{
    // Los DTOs HTTP se compilan también para el navegador: no pueden arrastrar el servidor.
    [Fact]
    public void Los_DTOs_HTTP_no_dependen_del_servidor()
    {
        Types().That().ResideInAssembly(HttpContractsAssembly)
            .Should().NotDependOnAny(InAnyNamespace(
                "Logistica.Modules",
                "Logistica.SharedKernel",
                "Logistica.BuildingBlocks",
                "Microsoft.AspNetCore",
                "Microsoft.EntityFrameworkCore"))
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
    }

    // Las aplicaciones del navegador solo hablan con la API por HTTP, usando Logistica.Http.Contracts.
    [Fact]
    public void Las_aplicaciones_WebAssembly_no_dependen_de_los_modulos()
    {
        Types().That().ResideInAssembly(WebAssemblyApps[0], WebAssemblyApps[1..])
            .Should().NotDependOnAny(InAnyNamespace(
                "Logistica.Modules",
                "Logistica.SharedKernel",
                "Logistica.BuildingBlocks",
                "Logistica.Backoffice"))
            .Check(Architecture);
    }
}

using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static Logistica.ArchitectureTests.LogisticaArchitecture;

namespace Logistica.ArchitectureTests;

// Reglas del código compartido (ADR-0001, addendum 1).
public class BuildingBlocksDependencyTests
{
    [Fact]
    public void SharedKernel_no_depende_de_frameworks()
    {
        Types().That().ResideInAssembly(SharedKernelAssembly)
            .Should().NotDependOnAny(InAnyNamespace(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Extensions",
                "Npgsql",
                "Logistica.BuildingBlocks"))
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
    }

    [Fact]
    public void BuildingBlocks_no_dependen_de_los_modulos()
    {
        Types().That().ResideInAssembly(SharedKernelAssembly, BuildingBlocksInfrastructureAssembly)
            .Should().NotDependOnAny(InNamespace("Logistica.Modules"))
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
    }
}

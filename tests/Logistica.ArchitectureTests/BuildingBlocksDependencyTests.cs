using System.Reflection;
using NetArchTest.Rules;

namespace Logistica.ArchitectureTests;

// Reglas del código compartido (ADR-001, addendum 1).
public class BuildingBlocksDependencyTests
{
    private const string SharedKernel = "Logistica.SharedKernel";
    private const string BuildingBlocksInfrastructure = "Logistica.BuildingBlocks.Infrastructure";

    [Fact]
    public void SharedKernel_no_depende_de_frameworks()
    {
        var result = Types.InAssembly(Assembly.Load(SharedKernel))
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Extensions",
                "Npgsql",
                BuildingBlocksInfrastructure)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Theory]
    [InlineData(SharedKernel)]
    [InlineData(BuildingBlocksInfrastructure)]
    public void BuildingBlocks_no_dependen_de_los_modulos(string assembly)
    {
        var result = Types.InAssembly(Assembly.Load(assembly))
            .ShouldNot().HaveDependencyOn("Logistica.Modules")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(TestResult result) =>
        "Tipos que violan la regla: " + string.Join(", ", result.FailingTypeNames ?? []);
}

using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.Slices.SliceRuleDefinition;
using static Logistica.ArchitectureTests.LogisticaArchitecture;

namespace Logistica.ArchitectureTests;

// Reglas de dependencia del ADR-0001, sección 2.4, y de su addendum 2.
public class ModuleDependencyTests
{
    public static TheoryData<string> ModuleNames => new(AllModules);

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Domain_no_depende_de_otras_capas_ni_de_frameworks(string module)
    {
        InNamespace($"Logistica.Modules.{module}.Domain")
            .Should().NotDependOnAny(InAnyNamespace(
                $"Logistica.Modules.{module}.Application",
                $"Logistica.Modules.{module}.Infrastructure",
                $"Logistica.Modules.{module}.Presentation",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore"))
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Application_no_depende_de_Infrastructure_Presentation_ni_frameworks(string module)
    {
        InNamespace($"Logistica.Modules.{module}.Application")
            .Should().NotDependOnAny(InAnyNamespace(
                $"Logistica.Modules.{module}.Infrastructure",
                $"Logistica.Modules.{module}.Presentation",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore"))
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
    }

    // ASP.NET Core solo se permite en Presentation y en la clase de entrada del módulo.
    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Infrastructure_no_depende_de_Presentation_ni_de_AspNetCore(string module)
    {
        InNamespace($"Logistica.Modules.{module}.Infrastructure")
            .Should().NotDependOnAny(InAnyNamespace(
                $"Logistica.Modules.{module}.Presentation",
                "Microsoft.AspNetCore"))
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Presentation_no_depende_de_Infrastructure(string module)
    {
        InNamespace($"Logistica.Modules.{module}.Presentation")
            .Should().NotDependOnAny(InNamespace($"Logistica.Modules.{module}.Infrastructure"))
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
    }

    // Cada módulo es un slice; como los Contracts no están cargados, usarlos está permitido.
    [Fact]
    public void Los_modulos_no_dependen_entre_si()
    {
        Slices().Matching("Logistica.Modules.(*)..")
            .Should().NotDependOnEachOther()
            .Check(Architecture);
    }
}

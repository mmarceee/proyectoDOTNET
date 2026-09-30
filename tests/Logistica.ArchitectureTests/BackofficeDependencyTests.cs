using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static Logistica.ArchitectureTests.LogisticaArchitecture;

namespace Logistica.ArchitectureTests;

// Reglas de las páginas del Backoffice (ADR-0001, addendum 4).
public class BackofficeDependencyTests
{
    // Razor compila cada .cshtml en una clase de este namespace, fuera del namespace del módulo,
    // por lo que las reglas por namespace de ModuleDependencyTests no las alcanzan.
    private const string GeneratedPagesNamespace = "AspNetCoreGeneratedDocument";

    public static TheoryData<string> ModuleNames => new(AllModules);

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Las_paginas_de_un_modulo_no_dependen_de_Infrastructure_ni_de_otros_modulos(string module)
    {
        Types().That().ResideInAssembly(ModuleAssembly(module))
            .And().ResideInNamespace(GeneratedPagesNamespace)
            .Should().NotDependOnAny(InAnyNamespace(
                [.. InternalsOfOtherModules(module), $"Logistica.Modules.{module}.Infrastructure"]))
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
    }

    // La biblioteca del Backoffice solo aporta layout y páginas comunes; los casos de uso viven en los módulos.
    [Fact]
    public void El_Backoffice_no_depende_de_los_modulos()
    {
        Types().That().ResideInAssembly(BackofficeAssembly)
            .Should().NotDependOnAny(InNamespace("Logistica.Modules"))
            .WithoutRequiringPositiveResults()
            .Check(Architecture);
    }
}

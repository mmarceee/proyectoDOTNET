using System.Reflection;
using NetArchTest.Rules;

namespace Logistica.ArchitectureTests;

// Reglas de dependencia del ADR-001, sección 2.4.
public class ModuleDependencyTests
{
    private static readonly string[] Modules =
        ["Administracion", "Envios", "Planificacion", "Ejecucion", "Seguimiento", "Deposito"];

    public static TheoryData<string> ModuleNames => new(Modules);

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Domain_no_depende_de_Application_Infrastructure_ni_frameworks(string module)
    {
        var result = Types.InAssembly(LoadModule(module))
            .That().ResideInNamespace($"Logistica.Modules.{module}.Domain")
            .ShouldNot().HaveDependencyOnAny(
                $"Logistica.Modules.{module}.Application",
                $"Logistica.Modules.{module}.Infrastructure",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Application_no_depende_de_Infrastructure(string module)
    {
        var result = Types.InAssembly(LoadModule(module))
            .That().ResideInNamespace($"Logistica.Modules.{module}.Application")
            .ShouldNot().HaveDependencyOnAny(
                $"Logistica.Modules.{module}.Infrastructure",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Un_modulo_solo_usa_los_Contracts_de_otro_modulo(string module)
    {
        var internalsOfOtherModules = Modules
            .Where(other => other != module)
            .SelectMany(other => new[]
            {
                $"Logistica.Modules.{other}.Domain",
                $"Logistica.Modules.{other}.Application",
                $"Logistica.Modules.{other}.Infrastructure",
            })
            .ToArray();

        var result = Types.InAssembly(LoadModule(module))
            .ShouldNot().HaveDependencyOnAny(internalsOfOtherModules)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static Assembly LoadModule(string module) => Assembly.Load($"Logistica.Modules.{module}");

    private static string Describe(TestResult result) =>
        "Tipos que violan la regla: " + string.Join(", ", result.FailingTypeNames ?? []);
}

using static Logistica.ArchitectureTests.LogisticaArchitecture;

namespace Logistica.ArchitectureTests;

// ADR-0001 §2.3: las reglas de dependencia no detectan casos fuera de Features.
public class ModuleOrganizationTests
{
    public static TheoryData<string> ModuleNames => new(AllModules);

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Los_handlers_de_aplicacion_se_organizan_por_feature(string module)
    {
        var application = $"Logistica.Modules.{module}.Application";
        var handlers = ModuleAssembly(module).GetTypes().Where(t =>
            t.Namespace?.StartsWith(application + ".", StringComparison.Ordinal) == true &&
            t.Name.EndsWith("Handler", StringComparison.Ordinal));

        Assert.All(handlers, handler =>
        {
            Assert.StartsWith(application + ".Features.", handler.Namespace!);
            Assert.False(handler.IsPublic);
        });
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void La_api_interna_del_modulo_se_implementa_en_Application(string module)
    {
        var contract = $"Logistica.Modules.{module}.Contracts.I{module}ModuleApi";
        var implementations = ModuleAssembly(module).GetTypes().Where(t =>
            t.IsClass && t.GetInterfaces().Any(i => i.FullName == contract));

        Assert.All(implementations, implementation =>
        {
            Assert.Equal($"Logistica.Modules.{module}.Application", implementation.Namespace);
            Assert.False(implementation.IsPublic);
        });
    }
}

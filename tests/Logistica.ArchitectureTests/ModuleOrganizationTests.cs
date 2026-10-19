using static Logistica.ArchitectureTests.LogisticaArchitecture;

namespace Logistica.ArchitectureTests;

// ADR-0001 §2.3: las reglas de dependencia no detectan casos fuera de Features.
public class ModuleOrganizationTests
{
    public static TheoryData<string> ModuleNames => new(AllModules);

    [Theory]
    [InlineData("Envios")]
    [InlineData("Planificacion")]
    public void Las_acciones_entre_modulos_se_exponen_por_ModuleApi(string module)
    {
        var contracts = System.Reflection.Assembly.Load($"Logistica.Modules.{module}.Contracts");
        var interfacesDeLectura = contracts.GetTypes().Where(t => t.IsPublic && t.IsInterface
            && t.Name != $"I{module}ModuleApi");

        // Los lectores existentes de CU-01/CU-50 no pueden convertirse en una API paralela de escritura.
        string[] prefijosDeLectura = ["Consultar", "Obtener", "Contar", "Reconocer"];
        Assert.All(interfacesDeLectura.SelectMany(t => t.GetMethods()), method =>
            Assert.True(prefijosDeLectura.Any(p => method.Name.StartsWith(p, StringComparison.Ordinal)),
                $"La acción {method.DeclaringType!.Name}.{method.Name} debe pertenecer a I{module}ModuleApi (ADR-0001 §2.2)."));
    }

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

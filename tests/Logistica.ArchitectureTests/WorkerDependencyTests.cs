using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using static Logistica.ArchitectureTests.LogisticaArchitecture;

namespace Logistica.ArchitectureTests;

// El Worker ejecuta el módulo Seguimiento y se comunica con los demás sólo por la cola (casos de uso, sección 3).
public class WorkerDependencyTests
{
    [Fact]
    public void El_Worker_solo_usa_el_modulo_Seguimiento_y_los_Contracts_de_los_demas()
    {
        Types().That().ResideInAssembly(WorkerAssembly)
            .Should().NotDependOnAny(InAnyNamespace(InternalsOfOtherModules("Seguimiento")))
            .Check(Architecture);
    }
}

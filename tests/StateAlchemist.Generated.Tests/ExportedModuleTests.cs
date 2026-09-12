using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.ExportingLibrary;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests;

/// <summary>
/// D25 end to end: a machine that declares nothing but its root gets its transitions from a referenced library
/// that exports a module, and the generated machine runs them.
/// </summary>
public class ExportedModuleTests
{
    [Test]
    public async Task AMachineBuiltFromAnExportedModuleRuns()
    {
        var machine = new ExportedMachine();
        await machine.StartAsync();
        await machine.FireAsync((byte)9);
        await machine.FireAsync((byte)9);

        machine.TryGetPingRoot(out var root);
        await Assert.That(root.Pings).IsEqualTo(2);
        await Assert.That(machine.IsIn<PingIdle>()).IsTrue();
    }

    [Test]
    public async Task ItsDefinitionNamesTheExportedTransition()
    {
        await Assert.That(ExportedMachine.Definition.Transitions.Select(t => t.Name)).IsEquivalentTo(new[] { "ExportedPingModule.Ping" });
    }
}

using System.Linq;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests;

/// <summary>
/// The diagrams the generator writes as constants. They are checked against the machine's own definition rather
/// than against fixed text: a diagram that agrees with <see cref="MachineDefinition"/> cannot drift from the code,
/// and that agreement is the only thing a diagram has to get right.
/// </summary>
public class DiagramTests
{
    [Test]
    public async Task TheMermaidDiagramHasEveryStateAndEveryTransition()
    {
        var definition = TelnetMachine.Definition;
        var diagram = TelnetMachine.Mermaid;
        await Assert.That(diagram).StartsWith("stateDiagram-v2");
        foreach (var state in definition.States)
        {
            await Assert.That(diagram).Contains(Name(state.Type.Name)).Because($"'{state.Name}' must appear");
        }

        await Assert.That(diagram.Split('\n').Count(line => line.Contains("-->") && line.Contains(':'))).IsEqualTo(definition.Transitions.Count);
    }

    /// <summary>A composite state is a Mermaid block with its initial child marked.</summary>
    [Test]
    public async Task TheMermaidDiagramNestsCompositeStatesAndMarksTheirInitialChild()
    {
        await Assert.That(TelnetMachine.Mermaid).Contains("state SubNegotiation {");
        await Assert.That(TelnetMachine.Mermaid).Contains("[*] --> AwaitingOption");
    }

    [Test]
    public async Task TheDotDiagramIsADigraphWithAClusterPerCompositeState()
    {
        var diagram = TelnetMachine.Dot;
        await Assert.That(diagram).StartsWith("digraph TelnetMachine {");
        await Assert.That(diagram).Contains("subgraph cluster_SubNegotiation {");
        await Assert.That(diagram).Contains("label=\"SubNegotiation\";");
        await Assert.That(diagram.Split('\n').Count(line => line.Contains("->"))).IsEqualTo(TelnetMachine.Definition.Transitions.Count);
        await Assert.That(diagram.Count(c => c == '{')).IsEqualTo(diagram.Count(c => c == '}'));
    }

    /// <summary>A run and a decision say so, so a reader can see where a batch is consumed at once.</summary>
    [Test]
    public async Task RunsAndDecisionsAreLabelledAsSuch()
    {
        await Assert.That(RunsMachine.Mermaid).Contains(" run");
        await Assert.That(DecidingMachine.Mermaid).Contains(" decide");
    }

    private static string Name(string type) => type;
}

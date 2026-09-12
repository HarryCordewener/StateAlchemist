using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

        await Assert.That(diagram.Split('\n').Count(line => line.Contains("-->") && line.Contains(':'))).IsEqualTo(Expected(definition));
    }

    /// <summary>
    /// A decision is one arrow per outcome, not one arrow. It does not know its target when the trigger arrives, so
    /// drawing it as a stay leaves the states only its outcomes reach with nothing pointing at them — which is how
    /// the door sample's <c>Unlocked</c> used to look unreachable.
    /// </summary>
    [Test]
    public async Task ADecisionIsDrawnAsOneArrowPerOutcome()
    {
        var definition = DecidingMachine.Definition;
        var diagram = DecidingMachine.Mermaid;

        var decisions = definition.Transitions.Where(t => t.IsDecision).ToList();
        await Assert.That(decisions).IsNotEmpty().Because("this machine is the one with decisions");

        foreach (var decision in decisions)
        {
            await Assert.That(decision.Outcomes).IsNotEmpty().Because($"'{decision.Name}' completes at least one outcome");
            foreach (var outcome in decision.Outcomes)
            {
                var arrow = $"{definition.States[decision.Source].Name} --> {definition.States[outcome.Target].Name} : " +
                            $"{decision.Trigger.Low} decide / {outcome.Name}";
                await Assert.That(diagram).Contains(arrow).Because($"'{decision.Name}' answering {outcome.Name} goes to {definition.States[outcome.Target].Name}");
            }
        }

        // And the states an outcome is the only way into are drawn as reachable.
        await Assert.That(diagram).Contains("--> Account :");
        await Assert.That(diagram.Split('\n').Count(line => line.Contains("-->") && line.Contains(':'))).IsEqualTo(Expected(definition));
    }

    /// <summary>One arrow per transition, except a decision, which is one per outcome.</summary>
    private static int Expected(MachineDefinition definition) =>
        definition.Transitions.Sum(t => t.Outcomes.Count == 0 ? 1 : t.Outcomes.Count);

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
        await Assert.That(diagram.Split('\n').Count(line => line.Contains("->"))).IsEqualTo(Expected(TelnetMachine.Definition));
        await Assert.That(diagram.Count(c => c == '{')).IsEqualTo(diagram.Count(c => c == '}'));
    }

    /// <summary>The machines whose diagrams are checked for shape.</summary>
    public static IEnumerable<Func<(string Name, string Mermaid)>> Diagrams() =>
    [
        () => ("Telnet", TelnetMachine.Mermaid),
        () => ("Recorder", RecorderMachine.Mermaid),
        () => ("Guards", GuardsMachine.Mermaid),
        () => ("Failures", FailuresMachine.Mermaid),
        () => ("Deciding", DecidingMachine.Mermaid),
        () => ("Runs", RunsMachine.Mermaid),
    ];

    /// <summary>
    /// Mermaid cannot parse a bare <c>state X</c> immediately followed by a nested <c>state Y {</c>: it reads the
    /// two lines as one state name and gives up on the diagram. Composite children are therefore written before
    /// leaf ones. This is a shape a reader cannot spot and a renderer refuses, so it is worth a test of its own.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(Diagrams))]
    public async Task NoLeafStateIsWrittenImmediatelyBeforeANestedBlock((string Name, string Mermaid) diagram)
    {
        var lines = diagram.Mermaid.Split('\n');
        var bad = Enumerable.Range(0, lines.Length - 1)
            .Where(i => Regex.IsMatch(lines[i], @"^\s*state \w+$")
                && Regex.IsMatch(lines[i + 1], @"^\s*state \w+ \{$"))
            .Select(i => lines[i].Trim() + " then " + lines[i + 1].Trim())
            .ToList();

        await Assert.That(bad).IsEmpty().Because($"{diagram.Name}: Mermaid would read these two lines as one state name");
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StateAlchemist.Samples.Crossing;
using StateAlchemist.Samples.Door;
using StateAlchemist.Samples.Lines;
using StateAlchemist.Samples.Phone;
using StateAlchemist.Samples.Telnet;
using TUnit.Core;

namespace StateAlchemist.Generated.Tests;

/// <summary>
/// The hand-drawn diagrams in <c>docs/guides/examples.md</c>, checked against the machines they draw. The
/// generator already emits a <c>Mermaid</c> constant that cannot drift; these are the annotated versions, which
/// say what each step *does*, and prose is exactly the part a compiler cannot check — so this does. Every state,
/// every parent, every <c>[Initial]</c> and every transition has to be drawn, once, with the right trigger.
/// </summary>
public class ExampleDiagramTests
{
    /// <summary>The machines the examples guide draws, by the name its <c>&lt;!-- diagram: … --&gt;</c> marker uses.</summary>
    public static IEnumerable<Func<(string Name, MachineDefinition Definition)>> Machines() =>
    [
        () => ("PhoneCall", PhoneCall.Definition),
        () => ("PedestrianCrossing", PedestrianCrossing.Definition),
        () => ("SampleTelnet", SampleTelnet.Definition),
        () => ("CardDoor", CardDoor.Definition),
        () => ("LineReader", LineReader.Definition),
    ];

    [Test]
    [MethodDataSource(nameof(Machines))]
    public async Task TheDiagramDrawsEveryStateAndItsParent((string Name, MachineDefinition Definition) machine)
    {
        var (name, definition) = machine;
        var diagram = Diagram(name);

        var drawn = Regex.Matches(diagram, @"^\s*state (?<name>\w+)( \{)?$", RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value)
            .ToList();
        await Assert.That(drawn.Count).IsEqualTo(drawn.Distinct().Count()).Because($"{name} draws a state twice");
        // The root is the diagram's outermost composite, so it is declared like any other.
        await Assert.That(drawn.OrderBy(s => s)).IsEquivalentTo(definition.States.Select(s => s.Name).OrderBy(s => s));

        foreach (var state in definition.States)
        {
            var children = definition.ChildrenOf(state).Select(c => c.Name).OrderBy(c => c).ToList();
            await Assert.That(Nested(diagram, state.Name).OrderBy(c => c)).IsEquivalentTo(children)
                .Because($"{name}: the children drawn inside {state.Name}");
        }
    }

    [Test]
    [MethodDataSource(nameof(Machines))]
    public async Task TheDiagramMarksEveryInitialChild((string Name, MachineDefinition Definition) machine)
    {
        var (name, definition) = machine;
        var diagram = Diagram(name);

        var expected = definition.States
            .Where(s => s.IsInitial)
            .Select(s => definition.States[s.Parent].Name + "/" + s.Name)
            .OrderBy(s => s)
            .ToList();
        var drawn = Regex.Matches(diagram, @"^\s*\[\*\] --> (?<name>\w+)$", RegexOptions.Multiline)
            .Select(m => Owner(diagram, m.Index) + "/" + m.Groups["name"].Value)
            .OrderBy(s => s)
            .ToList();

        await Assert.That(drawn).IsEquivalentTo(expected).Because($"{name}: the [*] arrows and the [Initial] states");
    }

    /// <summary>
    /// Every arrow is a transition the machine has, and every transition the machine has is an arrow. An arrow's
    /// label begins with the trigger — an enum member where the values are an enum, the number where they are
    /// not — and the prose that follows it is free.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(Machines))]
    public async Task EveryArrowIsATransitionAndEveryTransitionIsAnArrow((string Name, MachineDefinition Definition) machine)
    {
        var (name, definition) = machine;
        var diagram = Diagram(name);

        var expected = definition.Transitions
            .SelectMany(t => Tokens(definition, t))
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToList();

        var drawn = new List<string>();
        foreach (Match arrow in Regex.Matches(diagram, @"^\s*(?<from>\w+) --> (?<to>\w+) : (?<label>.+)$", RegexOptions.Multiline))
        {
            var label = arrow.Groups["label"].Value.Trim();
            // The label is "<trigger>" or "<trigger> …"; everything after the trigger is prose for the reader.
            var token = definition.Transitions
                .SelectMany(t => Tokens(definition, t).Select(a => a[(a.IndexOf(" : ", StringComparison.Ordinal) + 3)..]))
                .Distinct()
                .Where(t => label == t || label.StartsWith(t + " ", StringComparison.Ordinal))
                .OrderByDescending(t => t.Length)
                .FirstOrDefault() ?? label;
            drawn.Add($"{arrow.Groups["from"].Value} --> {arrow.Groups["to"].Value} : {token}");
        }

        await Assert.That(drawn.OrderBy(a => a, StringComparer.Ordinal)).IsEquivalentTo(expected)
            .Because($"{name}: the arrows drawn and the machine's transitions");
    }

    /// <summary>
    /// The arrows one transition owes the diagram: one, unless it is a decision, which owes one per outcome. A
    /// decision does not know its target when the trigger arrives, so the states only its outcomes reach would
    /// otherwise have nothing pointing at them.
    /// </summary>
    private static IEnumerable<string> Tokens(MachineDefinition definition, TransitionDefinition transition)
    {
        var source = definition.States[transition.Source].Name;
        if (transition.Outcomes.Count == 0)
        {
            yield return $"{source} --> {definition.States[transition.Target < 0 ? transition.Source : transition.Target].Name} : {Token(definition, transition)}";
            yield break;
        }

        foreach (var outcome in transition.Outcomes)
        {
            yield return $"{source} --> {definition.States[outcome.Target].Name} : {Token(definition, transition)} / {outcome.Name}";
        }
    }

    /// <summary>The trigger an arrow has to name: the enum member, the value, the range, or the event.</summary>
    private static string Token(MachineDefinition definition, TransitionDefinition transition)
    {
        var trigger = transition.Trigger;
        var value = trigger.Kind switch
        {
            TriggerKind.Value when definition.ValueType.IsEnum => Enum.GetName(definition.ValueType, Convert.ChangeType(trigger.Low, Enum.GetUnderlyingType(definition.ValueType), CultureInfo.InvariantCulture))!,
            TriggerKind.Value => trigger.Low.ToString(CultureInfo.InvariantCulture),
            TriggerKind.Range => $"{trigger.Low}..{trigger.High}",
            TriggerKind.Any => "any",
            _ => trigger.EventType!.Name,
        };
        return value + (transition.IsRun ? " run" : transition.IsDecision ? " decide" : string.Empty);
    }

    /// <summary>The states declared directly inside <paramref name="parent"/>'s block, if it has one.</summary>
    private static IEnumerable<string> Nested(string diagram, string parent)
    {
        var open = Regex.Match(diagram, @"^(?<indent>\s*)state " + parent + @" \{$", RegexOptions.Multiline);
        if (!open.Success)
        {
            yield break;
        }

        var inner = open.Groups["indent"].Value + "    ";
        foreach (Match child in Regex.Matches(diagram, "^" + inner + @"state (?<name>\w+)( \{)?$", RegexOptions.Multiline))
        {
            if (child.Index > open.Index && child.Index < End(diagram, open))
            {
                yield return child.Groups["name"].Value;
            }
        }
    }

    /// <summary>The innermost composite state whose block contains <paramref name="position"/>.</summary>
    private static string Owner(string diagram, int position)
    {
        var owner = "-";
        var depth = int.MinValue;
        foreach (Match open in Regex.Matches(diagram, @"^(?<indent>\s*)state (?<name>\w+) \{$", RegexOptions.Multiline))
        {
            if (open.Index < position && position < End(diagram, open) && open.Groups["indent"].Value.Length > depth)
            {
                depth = open.Groups["indent"].Value.Length;
                owner = open.Groups["name"].Value;
            }
        }

        return owner;
    }

    /// <summary>Where the block opened by <paramref name="open"/> closes: the <c>}</c> at its own indent.</summary>
    private static int End(string diagram, Match open)
    {
        var close = Regex.Match(diagram[open.Index..], "^" + open.Groups["indent"].Value + @"\}$", RegexOptions.Multiline);
        return close.Success ? open.Index + close.Index : diagram.Length;
    }

    /// <summary>The Mermaid block marked <c>&lt;!-- diagram: name --&gt;</c> in the examples guide.</summary>
    private static string Diagram(string name, [CallerFilePath] string here = "")
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "docs", "guides", "examples.md"));
        var markdown = File.ReadAllText(path).Replace("\r\n", "\n");
        var block = Regex.Match(markdown, @"<!-- diagram: " + name + @" -->\n```mermaid\n(?<diagram>.*?)```", RegexOptions.Singleline);
        return block.Success
            ? block.Groups["diagram"].Value
            : throw new InvalidOperationException($"docs/guides/examples.md has no '<!-- diagram: {name} -->' block.");
    }
}

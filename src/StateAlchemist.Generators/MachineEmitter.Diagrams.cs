using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    /// <summary>
    /// The machine as two <c>const string</c>s: Mermaid for a README or a docs page, Dot for Graphviz. Both are
    /// written at compile time from the model the machine runs, so they cannot drift from the code, and reading one
    /// costs nothing.
    /// </summary>
    private void WriteDiagrams()
    {
        _w.Line();
        _w.Line("/// <summary>The machine as a Mermaid state diagram.</summary>");
        _w.Line($"public const string Mermaid = {Literal(Mermaid())};");
        _w.Line();
        _w.Line("/// <summary>The machine as a Graphviz digraph.</summary>");
        _w.Line($"public const string Dot = {Literal(Dot())};");
    }

    /// <summary>A Mermaid <c>stateDiagram-v2</c>: composite states for the hierarchy, one arrow per transition.</summary>
    private string Mermaid()
    {
        var lines = new List<string> { "stateDiagram-v2" };
        void WriteState(int index, string indent)
        {
            var children = _model.States.Where(s => s.Parent == index).ToList();
            var name = _stateIds[index];
            if (children.Count == 0)
            {
                lines.Add($"{indent}state {name}");
                return;
            }

            lines.Add($"{indent}state {name} {{");
            var initial = children.FirstOrDefault(c => c.IsInitial);
            if (initial is not null)
            {
                lines.Add($"{indent}    [*] --> {_stateIds[initial.Index]}");
            }

            // Composites before leaves: Mermaid cannot parse a bare `state X` immediately followed by a nested
            // `state Y {`, and reads the two as one name. Ordering this way is the whole fix, and it is stable
            // because the states themselves are in index order within each group.
            foreach (var child in children.Where(c => _model.States.Any(s => s.Parent == c.Index))
                         .Concat(children.Where(c => !_model.States.Any(s => s.Parent == c.Index))))
            {
                WriteState(child.Index, indent + "    ");
            }

            lines.Add($"{indent}}}");
        }

        WriteState(_hierarchy.Root, "    ");
        foreach (var arrow in Arrows())
        {
            lines.Add($"    {_stateIds[arrow.Source]} --> {_stateIds[arrow.Target]} : {arrow.Label}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>A Graphviz digraph: a cluster per composite state, an edge per transition.</summary>
    private string Dot()
    {
        var lines = new List<string> { $"digraph {_machine.Machine.Name} {{", "    rankdir=LR;", "    node [shape=box];" };
        void WriteState(int index, string indent)
        {
            var children = _model.States.Where(s => s.Parent == index).ToList();
            var name = _stateIds[index];
            if (children.Count == 0)
            {
                lines.Add($"{indent}{name};");
                return;
            }

            lines.Add($"{indent}subgraph cluster_{name} {{");
            lines.Add($"{indent}    label=\"{name}\";");
            foreach (var child in children)
            {
                WriteState(child.Index, indent + "    ");
            }

            lines.Add($"{indent}}}");
        }

        WriteState(_hierarchy.Root, "    ");
        foreach (var arrow in Arrows())
        {
            lines.Add($"    {_stateIds[arrow.Source]} -> {_stateIds[arrow.Target]} [label=\"{arrow.Label}\"];");
        }

        lines.Add("}");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// One arrow per transition — except a decision, which is one arrow per outcome. A decision does not know its
    /// target when the trigger arrives, so drawing it as a stay would leave the states only its outcomes reach
    /// with nothing pointing at them: the door sample's <c>Unlocked</c> is reached by exactly one thing, and that
    /// thing is an outcome. A decision whose outcomes did not resolve falls back to the stay.
    /// </summary>
    private IEnumerable<(int Source, int Target, string Label)> Arrows()
    {
        foreach (var transition in _model.Transitions)
        {
            var completions = transition.Decision?.Completions ?? [];
            if (completions.Count == 0)
            {
                yield return (transition.Source, transition.Target < 0 ? transition.Source : transition.Target, Label(transition));
                continue;
            }

            foreach (var completion in completions)
            {
                var outcome = completion.OutcomeType;
                yield return (transition.Source, completion.Target, $"{Label(transition)} / {outcome.Substring(outcome.LastIndexOf('.') + 1)}");
            }
        }
    }

    /// <summary>What an arrow says: its trigger, and whether it is a run or a decision.</summary>
    private static string Label(TransitionModel transition)
    {
        var trigger = transition.Trigger.Kind switch
        {
            MatchKind.Value => transition.Trigger.Low.ToString(System.Globalization.CultureInfo.InvariantCulture),
            MatchKind.Range => $"{transition.Trigger.Low}..{transition.Trigger.High}",
            MatchKind.Any => "any",
            _ => transition.Trigger.EventType is { } name ? name.Substring(name.LastIndexOf('.') + 1) : "event",
        };
        var kind = transition.IsRun ? " run" : transition.IsDecision ? " decide" : string.Empty;
        return trigger + kind;
    }
}

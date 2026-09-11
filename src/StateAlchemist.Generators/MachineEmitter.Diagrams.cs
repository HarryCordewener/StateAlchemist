using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    /// <summary>
    /// The machine as a diagram, twice, as <c>const string</c>s: Mermaid for a README or a docs page, Dot for
    /// Graphviz. Both are written at compile time from the same model the machine runs, so a diagram cannot drift
    /// from the code — and because they are constants, reading one costs nothing and needs no reflection.
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
            var state = _model.States[index];
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

            foreach (var child in children)
            {
                WriteState(child.Index, indent + "    ");
            }

            lines.Add($"{indent}}}");
        }

        WriteState(_hierarchy.Root, "    ");
        foreach (var transition in _model.Transitions)
        {
            var target = transition.Target < 0 ? transition.Source : transition.Target;
            lines.Add($"    {_stateIds[transition.Source]} --> {_stateIds[target]} : {Label(transition)}");
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
        foreach (var transition in _model.Transitions)
        {
            var target = transition.Target < 0 ? transition.Source : transition.Target;
            lines.Add($"    {_stateIds[transition.Source]} -> {_stateIds[target]} [label=\"{Label(transition)}\"];");
        }

        lines.Add("}");
        return string.Join("\n", lines);
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

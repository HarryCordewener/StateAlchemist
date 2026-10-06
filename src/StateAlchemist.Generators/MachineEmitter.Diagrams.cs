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

            // After the children, so a bare state is never followed by a nested one (above).
            foreach (var history in HistoryMoves.Where(m => m.State == index).Select(m => m.History))
            {
                lines.Add($"{indent}    state \"{HistoryLabel(history)}\" as {HistoryNode(index, history)}");
                lines.Add($"{indent}    {HistoryNode(index, history)} --> {_stateIds[initial!.Index]}");
            }

            lines.Add($"{indent}}}");
        }

        WriteState(_hierarchy.Root, "    ");
        foreach (var arrow in Arrows())
        {
            lines.Add($"    {_stateIds[arrow.Source]} --> {arrow.Target} : {arrow.Label}");
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

            foreach (var history in HistoryMoves.Where(m => m.State == index).Select(m => m.History))
            {
                var initial = children.First(c => c.IsInitial);
                lines.Add($"{indent}    {HistoryNode(index, history)} [label=\"{HistoryLabel(history)}\", shape=circle];");
                lines.Add($"{indent}    {HistoryNode(index, history)} -> {_stateIds[initial.Index]} [style=dashed];");
            }

            lines.Add($"{indent}}}");
        }

        WriteState(_hierarchy.Root, "    ");
        foreach (var arrow in Arrows())
        {
            lines.Add($"    {_stateIds[arrow.Source]} -> {arrow.Target} [label=\"{arrow.Label}\"];");
        }

        lines.Add("}");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// One arrow per transition — except a decision, which is one arrow per outcome. A decision does not know its
    /// target when the trigger arrives, so drawing it as a stay would leave the states only its outcomes reach
    /// with nothing pointing at them: the door sample's <c>Unlocked</c> is reached by exactly one thing, and that
    /// thing is an outcome. A decision whose outcomes did not resolve falls back to the stay. A move by history points
    /// at its target's history node.
    /// </summary>
    private IEnumerable<(int Source, string Target, string Label)> Arrows()
    {
        foreach (var transition in _model.Transitions)
        {
            var completions = transition.Decision?.Completions ?? [];
            if (completions.Count == 0)
            {
                yield return (transition.Source, transition.Target < 0 ? _stateIds[transition.Source] : ArrowTarget(transition.Target, transition.History), Label(transition));
                continue;
            }

            foreach (var completion in completions)
            {
                var outcome = completion.OutcomeType;
                yield return (transition.Source, ArrowTarget(completion.Target, completion.History), $"{Label(transition)} / {outcome.Substring(outcome.LastIndexOf('.') + 1)}");
            }
        }
    }

    private string ArrowTarget(int target, HistoryKind history) =>
        HistoryMoves.Contains((target, history)) ? HistoryNode(target, history) : _stateIds[target];

    /// <summary>The diagram node for <paramref name="state"/>'s history: <c>H</c> for shallow, <c>H*</c> for deep, as UML draws them.</summary>
    private string HistoryNode(int state, HistoryKind history) => $"{_stateIds[state]}_{(history == HistoryKind.Deep ? "DeepHistory" : "History")}";

    private static string HistoryLabel(HistoryKind history) => history == HistoryKind.Deep ? "H*" : "H";

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

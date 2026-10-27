using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    private List<(int State, HistoryKind History)>? _historyMoves;

    /// <summary>Every (target, history) some transition or decision outcome moves to, by target.</summary>
    private List<(int State, HistoryKind History)> HistoryMoves => _historyMoves ??= _model.Transitions
        .Where(t => t.History != HistoryKind.None && t.Kind != MoveKind.Stay)
        .Select(t => (t.Target, t.History))
        .Concat(_model.Transitions.SelectMany(t => t.Decision?.Completions ?? []).Where(c => c.History != HistoryKind.None).Select(c => (c.Target, c.History)))
        .Where(m => m.Item1 != _hierarchy.Root && !_hierarchy.IsLeaf(m.Item1))
        .Distinct()
        .OrderBy(m => m.Item1)
        .ThenBy(m => m.Item2)
        .ToList();

    /// <summary>The states whose last active leaf is recorded: those some move enters with history.</summary>
    private IEnumerable<int> HistoryStates => HistoryMoves.Select(m => m.State).Distinct();

    /// <summary>The field holding the leaf that was active when <paramref name="state"/> was last exited; the state itself until then.</summary>
    private static string HistoryField(int state) => "_h" + state.ToString(CultureInfo.InvariantCulture);

    /// <summary>The method that answers which leaf a move into <paramref name="state"/> with <paramref name="history"/> enters.</summary>
    private static string RecallName(int state, HistoryKind history) => $"Recall{state.ToString(CultureInfo.InvariantCulture)}{history}";

    private void WriteHistoryStorage()
    {
        foreach (var state in HistoryStates)
        {
            _w.Line($"private StateId {HistoryField(state)};");
        }
    }

    /// <summary>Nothing recorded yet: each history field holds its own state, which is never a leaf under it.</summary>
    private void WriteHistoryInitialization()
    {
        foreach (var state in HistoryStates)
        {
            _w.Line($"{HistoryField(state)} = StateId.{_stateIds[state]};");
        }
    }

    /// <summary>A move that exits a state with history records the leaf it left, at commit.</summary>
    private void WriteRecording(TransitionPath path)
    {
        foreach (var state in HistoryStates.Where(path.Exiting.Contains))
        {
            _w.Line($"{HistoryField(state)} = StateId.{_stateIds[path.Leaf]};");
        }
    }

    /// <summary>Stopping exits every state, so it records like a move does.</summary>
    private void WriteStopRecording()
    {
        foreach (var state in HistoryStates)
        {
            _w.Line($"if (IsIn(StateId.{_stateIds[state]})) {HistoryField(state)} = _leaf;");
        }
    }

    /// <summary>For each (state, history) used, the leaf a move into it enters now: a <c>switch</c> over what was recorded.</summary>
    private void WriteRecalls()
    {
        foreach (var (state, history) in HistoryMoves)
        {
            var initial = _hierarchy.InitialLeaf(state);
            _w.Line();
            _w.Line($"/// <summary>The leaf a move into <see cref=\"{S(state)}\"/> with {history.ToString().ToLowerInvariant()} history enters now.</summary>");
            using (_w.Block($"private StateId {RecallName(state, history)}()"))
            {
                var cases = _hierarchy.LeavesUnder(state)
                    .Select(leaf => (Leaf: leaf, Recalled: PathPlanner.Recall(_hierarchy, state, history, leaf)))
                    .Where(c => c.Recalled != initial)
                    .GroupBy(c => c.Recalled)
                    .ToList();
                if (cases.Count == 0)
                {
                    _w.Line($"return StateId.{_stateIds[initial]};");
                    continue;
                }

                using (_w.Block($"switch ({HistoryField(state)})"))
                {
                    foreach (var group in cases)
                    {
                        foreach (var c in group)
                        {
                            _w.Line($"case StateId.{_stateIds[c.Leaf]}:");
                        }

                        _w.Line($"    return StateId.{_stateIds[group.Key]};");
                    }

                    _w.Line($"default: return StateId.{_stateIds[initial]};");
                }
            }
        }
    }

    /// <summary>
    /// A move's steps. When it enters by history a leaf known only when it runs, one method per leaf it can enter,
    /// and a method under <paramref name="name"/> that picks among them by what was recorded.
    /// </summary>
    private void WriteMove(string name, string parameters, string arguments, TransitionModel transition, IReadOnlyList<TransitionPath> paths,
        int target, HistoryKind history, MethodModel? transform, IReadOnlyList<MethodModel> completed, string kind, string? outcome)
    {
        if (paths.Count == 1)
        {
            WriteSteps(name, parameters, transition, paths[0], transform, completed, kind, outcome);
            return;
        }

        string Recalled(TransitionPath path) => $"{name}_Recall{path.TargetLeaf.ToString(CultureInfo.InvariantCulture)}";
        _w.Line();
        _w.Line($"// {transition.Name}: {_model.States[paths[0].Leaf].Name} --[{transition.Trigger}]--> {_model.States[target].Name}, by {history.ToString().ToLowerInvariant()} history");
        using (_w.Block($"private {ValueTaskType} {name}({parameters})"))
        {
            using (_w.Block($"switch ({RecallName(target, history)}())"))
            {
                foreach (var path in paths.Skip(1))
                {
                    _w.Line($"case StateId.{_stateIds[path.TargetLeaf]}: return {Recalled(path)}({arguments});");
                }

                _w.Line($"default: return {Recalled(paths[0])}({arguments});");
            }
        }

        foreach (var path in paths)
        {
            WriteSteps(Recalled(path), parameters, transition, path, transform, completed, kind, outcome);
        }
    }

    /// <summary>
    /// The target type a <c>TransitionInfo</c> names before the move runs: the leaf it will enter, which a move by
    /// history knows only by asking what was recorded.
    /// </summary>
    private string TargetType(TransitionModel transition, TransitionPath path, string phase) =>
        phase == "Guard" && PathPlanner.RecallsAtRunTime(_hierarchy, path.Leaf, transition.Target, transition.History)
            ? $"TypeOf({RecallName(transition.Target, transition.History)}())"
            : $"typeof({S(path.TargetLeaf)})";
}

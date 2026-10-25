using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>
/// Final states, eventless and done transitions, and deferred events: a final state is a leaf with no transitions; an
/// eventless transition moves, and unguarded ones do not loop; a done transition has something to wait for; a state
/// does not defer an event it handles.
/// </summary>
public static class StatechartValidator
{
    /// <summary>The problems in <paramref name="model"/>.</summary>
    public static IReadOnlyList<ModelDiagnostic> Validate(MachineModel model, Hierarchy hierarchy)
    {
        var diagnostics = new List<ModelDiagnostic>();
        foreach (var state in model.States.Where(s => s.IsFinal))
        {
            if (state.IsRoot)
            {
                diagnostics.Add(new(DiagnosticCatalog.InvalidFinal, state.Location, state.Name, "is the root: mark one of its children [Final] instead"));
            }
            else if (!hierarchy.IsLeaf(state.Index))
            {
                diagnostics.Add(new(DiagnosticCatalog.InvalidFinal, state.Location, state.Name, "has children: only a leaf can be final"));
            }

            foreach (var transition in model.Transitions.Where(t => t.Source == state.Index).GroupBy(t => t.Name).Select(g => g.First()))
            {
                diagnostics.Add(new(DiagnosticCatalog.InvalidFinal, transition.Location, state.Name, $"is the source of '{transition.Name}': a final state has no transitions"));
            }
        }

        foreach (var transition in model.Transitions.Where(t => t.IsAlways))
        {
            if (transition.Kind == MoveKind.Stay)
            {
                diagnostics.Add(new(DiagnosticCatalog.InvalidEventless, transition.Location, transition.Name, "stays: an eventless transition must move to a state, set To"));
            }
            else if (transition.Kind == MoveKind.Reenter && !transition.IsGuarded)
            {
                diagnostics.Add(new(DiagnosticCatalog.InvalidEventless, transition.Location, transition.Name, "re-enters its own state without a guard, so it would fire forever"));
            }
        }

        diagnostics.AddRange(Cycles(model, hierarchy));

        foreach (var transition in model.Transitions.Where(t => t.IsDone).GroupBy(t => t.Name).Select(g => g.First()))
        {
            var source = transition.Source;
            var why = hierarchy.ParentOf(source) < 0 && !Invokes(model, source)
                ? $"'{model.States[source].Name}' is the root, whose final children complete the machine instead"
                : !hierarchy.ChildrenOf(source).Any(c => model.States[c].IsFinal) && !Invokes(model, source)
                    ? $"'{model.States[source].Name}' has no [Final] child and invokes no machine"
                    : null;
            if (why is not null)
            {
                diagnostics.Add(new(DiagnosticCatalog.DoneNeverFires, transition.Location, transition.Name, model.States[source].Name, why));
            }
        }

        foreach (var state in model.States)
        {
            foreach (var deferred in state.Defers.Distinct())
            {
                if (model.Transitions.FirstOrDefault(t => t.Source == state.Index && t.Trigger.Kind == MatchKind.Event && t.Trigger.EventType == deferred) is { } handler)
                {
                    diagnostics.Add(new(DiagnosticCatalog.InvalidDefer, state.Location, state.Name, ShortName(deferred), $"'{handler.Name}' handles it in the same state; handle it or defer it"));
                }
            }
        }

        foreach (var invoke in model.Invokes)
        {
            diagnostics.AddRange(CheckInvoke(model, hierarchy, invoke));
        }

        return diagnostics;
    }

    /// <summary>An invoke method returns a statechart and takes only what an <c>[Entered]</c> action of its state could.</summary>
    private static IEnumerable<ModelDiagnostic> CheckInvoke(MachineModel model, Hierarchy hierarchy, InvokeModel invoke)
    {
        var method = invoke.Method;
        if (invoke.ChildValueType is null)
        {
            yield return new(DiagnosticCatalog.InvalidInvoke, method.Location, method.FullName, "must return a type that implements IStatechart<TValue>");
        }

        var active = hierarchy.PathFromRoot(invoke.State);
        foreach (var parameter in method.Parameters)
        {
            var why = parameter.Kind switch
            {
                ParameterKind.State when !active.Contains(parameter.State) =>
                    $"takes '{parameter.Name}', but '{model.States[parameter.State].Name}' is not active in '{model.States[invoke.State].Name}'",
                ParameterKind.State or ParameterKind.Config when parameter.Passing is Passing.Ref or Passing.Out =>
                    $"takes '{parameter.Name}' by ref: it reads states and the configuration, take them by in",
                ParameterKind.State or ParameterKind.Context or ParameterKind.Config or ParameterKind.CancellationToken => null,
                _ => $"takes '{parameter.Name}': it may take only active states, the context, the configuration and a CancellationToken",
            };
            if (why is not null)
            {
                yield return new(DiagnosticCatalog.InvalidInvoke, parameter.Location ?? method.Location, method.FullName, why);
            }
        }
    }

    /// <summary>Whether <paramref name="state"/> invokes a machine, whose completion makes it done.</summary>
    private static bool Invokes(MachineModel model, int state) => model.Invokes.Any(i => i.State == state);

    /// <summary>
    /// Leaves whose eventless transitions certainly fire — a single unguarded candidate — chained into a loop. Guarded
    /// loops are possible and are stopped at run time.
    /// </summary>
    private static IEnumerable<ModelDiagnostic> Cycles(MachineModel model, Hierarchy hierarchy)
    {
        var resolver = new Resolver(model, hierarchy);
        var next = new Dictionary<int, (int Leaf, TransitionModel Transition)>();
        foreach (var leaf in hierarchy.Leaves)
        {
            var candidates = resolver.ForAlways(leaf);
            if (candidates.Count == 1 && !candidates[0].IsGuarded && candidates[0].Kind != MoveKind.Stay)
            {
                next[leaf] = (PathPlanner.Plan(hierarchy, candidates[0], leaf).TargetLeaf, candidates[0]);
            }
        }

        var reported = new HashSet<int>();
        foreach (var start in next.Keys.OrderBy(l => l))
        {
            var seen = new List<int>();
            var leaf = start;
            while (next.ContainsKey(leaf) && !seen.Contains(leaf))
            {
                seen.Add(leaf);
                leaf = next[leaf].Leaf;
            }

            if (!next.ContainsKey(leaf) || leaf != start || reported.Contains(start))
            {
                continue;
            }

            foreach (var member in seen)
            {
                reported.Add(member);
            }

            var first = next[start].Transition;
            var loop = string.Join(" -> ", seen.Concat([start]).Select(l => model.States[l].Name));
            yield return new ModelDiagnostic(DiagnosticCatalog.InvalidEventless, first.Location, first.Name, $"is part of a loop of unguarded eventless transitions, {loop}, which would fire forever");
        }
    }

    private static string ShortName(string typeName)
    {
        var at = typeName.LastIndexOfAny(['.', '+']);
        return at < 0 ? typeName : typeName.Substring(at + 1);
    }
}

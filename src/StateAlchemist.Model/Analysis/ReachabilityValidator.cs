using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>Warns about states no sequence of transitions reaches from the initial leaf (SALCH0502). Guards are assumed to pass.</summary>
public static class ReachabilityValidator
{
    /// <summary>One warning per unreachable state.</summary>
    public static IReadOnlyList<ModelDiagnostic> Validate(MachineModel model, Hierarchy hierarchy)
    {
        var start = hierarchy.InitialLeaf(hierarchy.Root);
        var leaves = new HashSet<int> { start };
        var pending = new Queue<int>();
        pending.Enqueue(start);
        while (pending.Count > 0)
        {
            var leaf = pending.Dequeue();
            foreach (var transition in model.Transitions.Where(t => hierarchy.IsAncestorOrSelf(t.Source, leaf)))
            {
                IEnumerable<int> targets = transition.IsDecision
                    ? transition.Decision!.Completions.Select(c => c.Target)
                    : transition.Kind == MoveKind.Stay ? [] : [transition.Target];
                foreach (var target in targets)
                {
                    var next = PathPlanner.Move(hierarchy, leaf, target).TargetLeaf;
                    if (leaves.Add(next))
                    {
                        pending.Enqueue(next);
                    }
                }
            }
        }

        var reached = new HashSet<int>(leaves.SelectMany(hierarchy.PathFromRoot));
        return model.States
            .Where(state => !reached.Contains(state.Index))
            .Select(state => new ModelDiagnostic(DiagnosticCatalog.UnreachableState, state.Location, state.Name))
            .ToList();
    }
}

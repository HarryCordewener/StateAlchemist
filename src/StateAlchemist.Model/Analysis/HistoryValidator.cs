using System.Collections.Generic;

namespace StateAlchemist.Model;

/// <summary>
/// Checks that every move with history has something to recall (SALCH0210): its target has children in this machine
/// and is not the root, which is never exited.
/// </summary>
public static class HistoryValidator
{
    /// <summary>One error per transition or outcome that asks for history it cannot have.</summary>
    public static IReadOnlyList<ModelDiagnostic> Validate(MachineModel model, Hierarchy hierarchy)
    {
        var diagnostics = new List<ModelDiagnostic>();
        foreach (var transition in model.Transitions)
        {
            if (transition.History != HistoryKind.None)
            {
                var problem = transition.Kind == MoveKind.Stay ? "it is a stay, which enters nothing" : Problem(model, hierarchy, transition.Target);
                if (problem is not null)
                {
                    diagnostics.Add(new(DiagnosticCatalog.InvalidHistory, transition.Location, transition.Name, problem));
                }
            }

            foreach (var completion in transition.Decision?.Completions ?? [])
            {
                if (completion.History != HistoryKind.None && Problem(model, hierarchy, completion.Target) is { } problem)
                {
                    diagnostics.Add(new(DiagnosticCatalog.InvalidHistory, completion.Complete.Location, completion.Complete.FullName, problem));
                }
            }
        }

        return diagnostics;
    }

    private static string? Problem(MachineModel model, Hierarchy hierarchy, int target) =>
        target == hierarchy.Root ? $"its target '{model.States[target].Name}' is the root, which is never exited"
        : hierarchy.IsLeaf(target) ? $"its target '{model.States[target].Name}' has no children in this machine"
        : null;
}

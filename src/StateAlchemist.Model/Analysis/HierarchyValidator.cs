using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>Checks that states form one tree whose parents each have exactly one <c>[Initial]</c> child (SALCH0003, SALCH0004).</summary>
public static class HierarchyValidator
{
    /// <summary>Every hierarchy problem; empty when <see cref="Hierarchy"/> can be built.</summary>
    public static IReadOnlyList<ModelDiagnostic> Validate(IReadOnlyList<StateModel> states)
    {
        var diagnostics = new List<ModelDiagnostic>();
        var roots = states.Where(s => s.IsRoot).ToList();
        if (roots.Count == 0 && states.Count > 0)
        {
            diagnostics.Add(new(DiagnosticCatalog.InvalidHierarchy, states[0].Location, states[0].Name, "belongs to a machine with no root"));
        }

        foreach (var extra in roots.Skip(1))
        {
            diagnostics.Add(new(DiagnosticCatalog.InvalidHierarchy, extra.Location, extra.Name, $"is a second root; '{roots[0].Name}' is already the root"));
        }

        foreach (var state in states)
        {
            if (!state.IsRoot && state.Parent >= states.Count)
            {
                diagnostics.Add(new(DiagnosticCatalog.InvalidHierarchy, state.Location, state.Name, "has a parent that is not in the machine"));
            }
        }

        if (diagnostics.Count > 0)
        {
            return diagnostics;
        }

        foreach (var state in states)
        {
            var steps = 0;
            for (var at = state.Parent; at >= 0 && steps <= states.Count; at = states[at].Parent)
            {
                steps++;
            }

            if (steps > states.Count)
            {
                diagnostics.Add(new(DiagnosticCatalog.InvalidHierarchy, state.Location, state.Name, "has a parent chain that loops"));
            }
        }

        if (diagnostics.Count > 0)
        {
            return diagnostics;
        }

        foreach (var state in states)
        {
            var children = states.Where(c => c.Parent == state.Index).ToList();
            if (children.Count == 0)
            {
                continue;
            }

            var initial = children.Count(c => c.IsInitial);
            if (initial != 1)
            {
                diagnostics.Add(new(DiagnosticCatalog.InitialChild, state.Location, state.Name, initial));
            }
        }

        return diagnostics;
    }
}

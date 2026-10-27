using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>
/// Checks what a join may declare (SALCH0106). A join has no <c>Guard</c>: a guard that refused the last arrival
/// would leave the join complete with nothing left to retry it. Nor is it a decision.
/// </summary>
public static class JoinValidator
{
    /// <summary>Every join problem, once per join.</summary>
    public static IReadOnlyList<ModelDiagnostic> Validate(MachineModel model)
    {
        var diagnostics = new List<ModelDiagnostic>();
        foreach (var join in model.Transitions.Where(t => t.IsJoin).GroupBy(t => t.Name).Select(g => g.First()))
        {
            if (join.IsGuarded)
            {
                diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, join.Location, join.Name, "is a join, which cannot have a Guard"));
            }

            if (join.IsDecision)
            {
                diagnostics.Add(new(DiagnosticCatalog.InvalidTransition, join.Location, join.Name, "is a join, which cannot be a decision"));
            }
        }

        return diagnostics;
    }
}

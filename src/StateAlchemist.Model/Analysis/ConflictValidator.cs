using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>
/// Finds transitions and actions whose order would be undefined (SALCH0101, SALCH0102, SALCH0103). Transitions of
/// different match kinds never conflict — an exact value beats a range beats <c>[OnAny]</c>.
/// </summary>
public static class ConflictValidator
{
    /// <summary>Every conflict.</summary>
    public static IReadOnlyList<ModelDiagnostic> Validate(MachineModel model)
    {
        var diagnostics = new List<ModelDiagnostic>();
        foreach (var level in model.Transitions.GroupBy(t => t.Source))
        {
            var transitions = level.ToList();
            for (var i = 0; i < transitions.Count; i++)
            {
                for (var j = i + 1; j < transitions.Count; j++)
                {
                    var a = transitions[i];
                    var b = transitions[j];
                    if (a.Trigger.Kind != b.Trigger.Kind || !a.Trigger.Overlaps(b.Trigger))
                    {
                        continue;
                    }

                    var state = model.States[a.Source].Name;
                    var shared = Shared(a.Trigger, b.Trigger);
                    if (!a.IsGuarded && !b.IsGuarded)
                    {
                        diagnostics.Add(new(DiagnosticCatalog.ConflictingTransitions, b.Location, a.Name, b.Name, shared, state));
                    }
                    else if (a.IsGuarded && b.IsGuarded && a.Order == b.Order)
                    {
                        diagnostics.Add(new(DiagnosticCatalog.AmbiguousGuardOrder, b.Location, a.Name, b.Name, shared, state, a.Order));
                    }
                }
            }
        }

        foreach (var phase in model.StateActions.GroupBy(a => (a.State, a.Phase)))
        {
            var actions = phase.ToList();
            for (var i = 0; i < actions.Count; i++)
            {
                for (var j = i + 1; j < actions.Count; j++)
                {
                    var a = actions[i];
                    var b = actions[j];
                    if (a.Module != b.Module && a.Order == b.Order)
                    {
                        diagnostics.Add(new(DiagnosticCatalog.AmbiguousActionOrder, b.Method.Location, a.Method.FullName, b.Method.FullName,
                            model.States[a.State].Name, a.Phase == ActionPhase.Exited ? "exited" : "entered", a.Order));
                    }
                }
            }
        }

        return diagnostics;
    }

    private static string Shared(TriggerModel a, TriggerModel b) => a.Kind switch
    {
        MatchKind.Range => string.Format(CultureInfo.InvariantCulture, "{0}..{1}", Math.Max(a.Low, b.Low), Math.Min(a.High, b.High)),
        MatchKind.Any => "any value",
        _ => a.ToString(),
    };
}

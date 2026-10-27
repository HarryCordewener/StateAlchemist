using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>
/// Checks the states a timer's <c>Delay</c> reads (SALCH0202, SALCH0204). It runs as its state is entered, from any
/// transition that enters it, so it may read that state and its ancestors, and only read them.
/// </summary>
public static class TimerValidator
{
    /// <summary>Every problem with a <c>Delay</c>'s state parameters.</summary>
    public static IReadOnlyList<ModelDiagnostic> Validate(MachineModel model, Hierarchy hierarchy)
    {
        var diagnostics = new List<ModelDiagnostic>();
        foreach (var transition in model.Transitions.Where(t => t.IsTimer && t.Delay is not null).GroupBy(t => t.Name).Select(g => g.First()))
        {
            var delay = transition.Delay!;
            foreach (var parameter in delay.Parameters.Where(p => p.Kind == ParameterKind.State))
            {
                if (!hierarchy.IsAncestorOrSelf(parameter.State, transition.Source))
                {
                    diagnostics.Add(new(DiagnosticCatalog.StateNotAvailable, delay.Location, parameter.Name, delay.FullName,
                        model.States[parameter.State].Name, $"is not active when '{model.States[transition.Source].Name}' is entered"));
                }
                else if (parameter.Passing == Passing.Ref)
                {
                    diagnostics.Add(new(DiagnosticCatalog.UnbindableParameter, delay.Location, parameter.Name, delay.FullName,
                        "a Delay reads state: take it as in"));
                }
            }
        }

        return diagnostics;
    }
}

using System.Collections.Generic;

namespace StateAlchemist.Model;

/// <summary>Checks the value type and that every value trigger fits it (SALCH0104, SALCH0105).</summary>
public static class TriggerValidator
{
    /// <summary>Every trigger problem.</summary>
    public static IReadOnlyList<ModelDiagnostic> Validate(MachineModel model)
    {
        var diagnostics = new List<ModelDiagnostic>();
        var options = model.Options;
        if (!options.ValueTypeSupported)
        {
            diagnostics.Add(new(DiagnosticCatalog.UnsupportedValueType, SourceSpan.None, options.ValueType));
            return diagnostics;
        }

        foreach (var transition in model.Transitions)
        {
            var trigger = transition.Trigger;
            if (trigger.Kind is MatchKind.Value or MatchKind.Range
                && (!options.Domain.Contains(trigger.Low) || !options.Domain.Contains(trigger.High)))
            {
                diagnostics.Add(new(DiagnosticCatalog.TriggerOutOfRange, transition.Location, transition.Name, trigger.ToString(), options.ValueType));
            }
        }

        return diagnostics;
    }
}

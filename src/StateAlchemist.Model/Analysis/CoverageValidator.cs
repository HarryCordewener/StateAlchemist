using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>Warns about leaves where some values match no transition at any level (SALCH0501).</summary>
public static class CoverageValidator
{
    /// <summary>One warning per leaf that leaves values unhandled.</summary>
    public static IReadOnlyList<ModelDiagnostic> Validate(MachineModel model, Hierarchy hierarchy)
    {
        var diagnostics = new List<ModelDiagnostic>();
        if (!model.Options.ValueTypeSupported)
        {
            return diagnostics;
        }

        var bySource = model.Transitions.ToLookup(t => t.Source);
        foreach (var leaf in hierarchy.Leaves)
        {
            var levels = hierarchy.PathFromRoot(leaf);
            var triggers = levels.SelectMany(state => bySource[state]).Select(t => t.Trigger).Where(t => t.IsValue).ToList();
            if (triggers.Any(t => t.Kind == MatchKind.Any))
            {
                continue;
            }

            var unhandled = model.Options.Domain.Values.LongCount(value => !triggers.Any(t => t.Matches(value)));
            if (unhandled > 0)
            {
                var state = model.States[leaf];
                diagnostics.Add(new(DiagnosticCatalog.UnhandledValues, state.Location, state.Name, unhandled));
            }
        }

        return diagnostics;
    }
}

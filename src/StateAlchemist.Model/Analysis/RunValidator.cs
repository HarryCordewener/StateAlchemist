using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>Checks run transitions' shape (SALCH0701) and what they swallow (SALCH0702).</summary>
public static class RunValidator
{
    /// <summary>Every run problem.</summary>
    public static IReadOnlyList<ModelDiagnostic> Validate(MachineModel model, Hierarchy hierarchy)
    {
        var diagnostics = new List<ModelDiagnostic>();
        foreach (var run in model.Transitions.Where(t => t.IsRun).GroupBy(t => t.Name).Select(g => g.First()))
        {
            var problem = Problem(model, run);
            if (problem is not null)
            {
                diagnostics.Add(new(DiagnosticCatalog.InvalidRun, run.Location, run.Name, problem));
            }
        }

        diagnostics.AddRange(Shadowed(model, hierarchy));
        return diagnostics;
    }

    /// <summary>
    /// A run that swallows a value one of its ancestors handles. A state's trigger wins over an ancestor's, which is
    /// what makes <c>[OnAny]</c> a state's "or else" — but a run takes a whole stretch of input in one call, so the
    /// ancestor's value does not merely lose: it disappears into the run rather than ending it, and the machine
    /// never leaves the state. Reported once per pair, wherever the run is declared.
    /// </summary>
    private static IEnumerable<ModelDiagnostic> Shadowed(MachineModel model, Hierarchy hierarchy)
    {
        var resolver = new Resolver(model, hierarchy);
        var reported = new HashSet<(int Run, int Shadowed)>();
        foreach (var run in model.Transitions.Where(t => t.IsRun && t.Kind == MoveKind.Stay))
        {
            var above = model.Transitions
                .Where(t => t.Trigger.Kind is MatchKind.Value or MatchKind.Range)
                .Where(t => t.Source != run.Source && hierarchy.IsAncestorOrSelf(t.Source, run.Source))
                .ToList();
            foreach (var leaf in hierarchy.LeavesUnder(run.Source))
            {
                foreach (var shadowed in above)
                {
                    var value = shadowed.Trigger.Low;
                    var candidates = resolver.ForValue(leaf, value);
                    if (candidates.Count == 0 || candidates[0].Index != run.Index || !reported.Add((run.Index, shadowed.Index)))
                    {
                        continue;
                    }

                    yield return new ModelDiagnostic(DiagnosticCatalog.RunShadowsTrigger, run.Location,
                        run.Name, shadowed.Name, model.States[shadowed.Source].Name, value, model.States[run.Source].Name);
                }
            }
        }
    }

    private static string? Problem(MachineModel model, TransitionModel run)
    {
        if (run.Kind != MoveKind.Stay)
        {
            return "a run transition must be a stay";
        }

        if (run.Trigger.Kind is not (MatchKind.Any or MatchKind.Range))
        {
            return "a run transition fires on [OnAny] or a range";
        }

        if (run.Guard is not null)
        {
            return "a run transition cannot have a Guard";
        }

        var parameters = run.Transform?.Parameters ?? [];
        var shapeIsRight = parameters.Count >= 2
            && parameters[0] is { Kind: ParameterKind.State, Passing: Passing.Ref } self && self.State == run.Source
            && parameters[1].Kind == ParameterKind.Run
            && parameters.Skip(2).All(p => p.Kind is ParameterKind.Config or ParameterKind.Context);
        return shapeIsRight
            ? null
            : $"its Transform must take (ref {model.States[run.Source].Name} self, ReadOnlySpan<{model.Options.ValueType}> run), optionally followed by the configuration and the context";
    }
}

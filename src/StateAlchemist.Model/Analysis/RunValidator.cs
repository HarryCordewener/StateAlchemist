using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>Checks run transitions' shape (SALCH0701).</summary>
public static class RunValidator
{
    /// <summary>Every run problem.</summary>
    public static IReadOnlyList<ModelDiagnostic> Validate(MachineModel model)
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

        return diagnostics;
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

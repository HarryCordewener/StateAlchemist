using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>
/// Checks every parameter that is not a state against what its method may take (SALCH0204). State parameters are
/// checked against their roles by <see cref="RoleValidator"/>.
/// </summary>
public static class BindingValidator
{
    private enum Use
    {
        Guard,
        Transform,
        Complete,
        Decide,
        Completed,
        StateAction,
    }

    /// <summary>Every binding problem.</summary>
    public static IReadOnlyList<ModelDiagnostic> Validate(MachineModel model)
    {
        var diagnostics = new List<ModelDiagnostic>();
        foreach (var transition in model.Transitions.GroupBy(t => t.Name).Select(g => g.First()))
        {
            if (transition.Guard is not null)
            {
                Check(model, transition, transition.Guard, Use.Guard, diagnostics);
            }

            if (transition.Transform is not null)
            {
                Check(model, transition, transition.Transform, Use.Transform, diagnostics);
            }

            if (transition.Decision?.Decider is { } decider)
            {
                Check(model, transition, decider, Use.Decide, diagnostics);
            }

            foreach (var completion in transition.Decision?.Completions ?? [])
            {
                Check(model, transition, completion.Complete, Use.Complete, diagnostics);
            }

            foreach (var completed in transition.Completed)
            {
                Check(model, transition, completed, Use.Completed, diagnostics);
            }
        }

        foreach (var action in model.StateActions)
        {
            Check(model, null, action.Method, Use.StateAction, diagnostics);
        }

        return diagnostics;
    }

    private static void Check(MachineModel model, TransitionModel? transition, MethodModel method, Use use, List<ModelDiagnostic> diagnostics)
    {
        foreach (var parameter in method.Parameters.Where(p => p.Kind != ParameterKind.State))
        {
            var problem = Problem(model, transition, method, use, parameter);
            if (problem is not null)
            {
                diagnostics.Add(new(DiagnosticCatalog.UnbindableParameter, method.Location, parameter.Name, method.FullName, problem));
            }
        }
    }

    private static string? Problem(MachineModel model, TransitionModel? transition, MethodModel method, Use use, ParameterModel parameter)
    {
        if (parameter.Passing == Passing.Out)
        {
            return "out parameters are not supported";
        }

        var trigger = transition?.Trigger;
        switch (parameter.Kind)
        {
            case ParameterKind.Unknown:
                return $"'{parameter.TypeName}' is not a state, the value type, an event, the configuration, the context, or a CancellationToken";
            case ParameterKind.Context:
                return null;
            case ParameterKind.Config:
                return parameter.Passing == Passing.Ref ? "the configuration is read-only: take it as in" : null;
            case ParameterKind.Value when use is Use.StateAction or Use.Complete:
                return $"{Describe(use)} cannot take the value";
            case ParameterKind.Value when trigger is { IsValue: false }:
                return "this transition fires on an event, not a value";
            case ParameterKind.Value when transition is { IsRun: true }:
                return "a run transition takes the run, not a single value";
            case ParameterKind.Value:
                return null;
            case ParameterKind.Run:
                return use == Use.Transform && transition is { IsRun: true } ? null : "only a [Run] transition's Transform takes a run";
            case ParameterKind.RunMemory:
                return use == Use.Completed && transition is { IsRun: true } ? null : "only a [Run] transition's Completed takes the run as memory";
            case ParameterKind.Event when use is Use.StateAction or Use.Complete:
                return $"{Describe(use)} cannot take the event";
            case ParameterKind.Event when trigger is not { Kind: MatchKind.Event } || trigger.Value.EventType != parameter.TypeName:
                return $"this transition fires on {trigger?.ToString() ?? "no trigger"}, not '{parameter.TypeName}'";
            case ParameterKind.Event:
                return null;
            case ParameterKind.Outcome:
                return use is Use.Complete or Use.Completed ? null : $"{Describe(use)} cannot take a decision outcome";
            case ParameterKind.CancellationToken:
                return use is Use.Completed or Use.StateAction || (use == Use.Decide && method.IsAsync)
                    ? null
                    : $"{Describe(use)} cannot take a CancellationToken";
            case ParameterKind.TransitionInfo:
                return use is Use.Completed or Use.StateAction ? null : $"{Describe(use)} cannot take the transition info";
            default:
                return null;
        }
    }

    private static string Describe(Use use) => use switch
    {
        Use.Guard => "a Guard",
        Use.Transform => "a Transform",
        Use.Complete => "a Complete",
        Use.Decide => "a Decide",
        Use.Completed => "a Completed",
        _ => "a state action",
    };
}

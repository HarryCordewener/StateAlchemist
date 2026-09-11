using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>
/// Checks declarations' shapes: state types, member visibility, phase signatures and names, the <c>Async</c>
/// suffix, decisions' outcomes, purity, and unchecked machines with async code (SALCH0001, 0002, 0203, 0205–0209,
/// 0401, 0402).
/// </summary>
public static class ShapeValidator
{
    private static readonly string[] AsyncPhasesThatMayNotBe = ["GuardAsync", "TransformAsync", "CompleteAsync"];

    /// <summary>Every shape problem.</summary>
    public static IReadOnlyList<ModelDiagnostic> Validate(MachineModel model)
    {
        var diagnostics = new List<ModelDiagnostic>();

        foreach (var state in model.States)
        {
            if (!state.IsPublicStruct)
            {
                diagnostics.Add(new(DiagnosticCatalog.InvalidState, state.Location, state.Name, "it is not a public struct"));
            }
            else if (state.ParentMarkers != 1)
            {
                diagnostics.Add(new(DiagnosticCatalog.InvalidState, state.Location, state.Name, $"it implements {state.ParentMarkers} parent markers"));
            }
        }

        foreach (var method in model.AllMethods.Where(m => !m.IsStatic || !m.IsPublic))
        {
            diagnostics.Add(new(DiagnosticCatalog.NotPublicStatic, method.Location, method.FullName));
        }

        foreach (var transition in model.Transitions.GroupBy(t => t.Name).Select(g => g.First()))
        {
            CheckTransition(model, transition, diagnostics);
        }

        foreach (var action in model.StateActions)
        {
            if (action.Method.Returns is not (ReturnShape.Void or ReturnShape.ValueTask or ReturnShape.Task))
            {
                diagnostics.Add(new(DiagnosticCatalog.InvalidPhaseSignature, action.Method.Location, action.Method.FullName, "return void or ValueTask"));
            }
        }

        if (model.Options.Concurrency == ConcurrencyMode.Unchecked && model.AllMethods.Any(m => m.IsAsync))
        {
            diagnostics.Add(new(DiagnosticCatalog.UncheckedWithAsync, SourceSpan.None, model.Name));
        }

        return diagnostics;
    }

    private static void CheckTransition(MachineModel model, TransitionModel transition, List<ModelDiagnostic> diagnostics)
    {
        if (transition.Guard is { } guard && guard.Returns != ReturnShape.Bool)
        {
            diagnostics.Add(new(DiagnosticCatalog.InvalidPhaseSignature, guard.Location, guard.FullName, "return bool synchronously"));
        }

        if (transition.Transform is { } transform && transform.Returns != ReturnShape.Void)
        {
            diagnostics.Add(new(DiagnosticCatalog.InvalidPhaseSignature, transform.Location, transform.FullName, "return void synchronously"));
        }

        foreach (var completed in transition.Completed)
        {
            CheckSuffix(completed, "Completed", "CompletedAsync", diagnostics);
            if (completed.Returns is ReturnShape.ValueTaskOfResult or ReturnShape.TaskOfResult or ReturnShape.Bool or ReturnShape.Other)
            {
                diagnostics.Add(new(DiagnosticCatalog.InvalidPhaseSignature, completed.Location, completed.FullName, "return void or ValueTask"));
            }
        }

        foreach (var member in transition.UnknownMembers)
        {
            var name = transition.Name + "." + member;
            if (AsyncPhasesThatMayNotBe.Contains(member))
            {
                diagnostics.Add(new(DiagnosticCatalog.AsyncSuffix, transition.Location, name, "cannot be async: it runs before the state changes"));
            }
            else
            {
                diagnostics.Add(new(DiagnosticCatalog.UnknownPhase, transition.Location, name));
            }
        }

        if (transition.Decision is { } decision)
        {
            CheckDecision(transition, decision, diagnostics);
        }

        if (model.Options.Purity == PurityMode.Strict)
        {
            var pure = new[] { transition.Guard, transition.Transform }
                .Concat(transition.Decision?.Completions.Select(c => c.Complete) ?? [])
                .OfType<MethodModel>();
            foreach (var method in pure.Where(m => m.Parameters.Any(p => p.Kind == ParameterKind.Context)))
            {
                diagnostics.Add(new(DiagnosticCatalog.ContextUnderStrictPurity, method.Location, method.FullName, model.Name));
            }
        }
    }

    private static void CheckDecision(TransitionModel transition, DecisionModel decision, List<ModelDiagnostic> diagnostics)
    {
        if (decision.Decide is not null && decision.DecideAsync is not null)
        {
            diagnostics.Add(new(DiagnosticCatalog.TwoDecideMethods, transition.Location, transition.Name));
        }
        else if (decision.Decider is null)
        {
            diagnostics.Add(new(DiagnosticCatalog.InvalidPhaseSignature, transition.Location, transition.Name, "declare Decide or DecideAsync"));
        }

        if (decision.Decide is { } decide)
        {
            CheckSuffix(decide, "Decide", "DecideAsync", diagnostics);
        }

        if (decision.DecideAsync is { } decideAsync)
        {
            CheckSuffix(decideAsync, "Decide", "DecideAsync", diagnostics);
            if (decideAsync.Returns is ReturnShape.ValueTask or ReturnShape.Task)
            {
                diagnostics.Add(new(DiagnosticCatalog.InvalidPhaseSignature, decideAsync.Location, decideAsync.FullName, "return ValueTask<TOutcome>"));
            }
        }

        foreach (var completion in decision.Completions)
        {
            if (completion.Complete.Returns != ReturnShape.Void)
            {
                diagnostics.Add(new(DiagnosticCatalog.InvalidPhaseSignature, completion.Complete.Location, completion.Complete.FullName, "return void synchronously"));
            }
        }

        foreach (var outcome in decision.Outcomes)
        {
            var count = decision.Completions.Count(c => c.OutcomeType == outcome);
            if (count != 1)
            {
                diagnostics.Add(new(DiagnosticCatalog.OutcomeCompletions, transition.Location, transition.Name, count, ShortName(outcome)));
            }
        }

        foreach (var completion in decision.Completions.Where(c => !decision.Outcomes.Contains(c.OutcomeType)))
        {
            diagnostics.Add(new(DiagnosticCatalog.UnknownOutcome, completion.Complete.Location, completion.Complete.FullName, ShortName(completion.OutcomeType), transition.Name));
        }
    }

    private static void CheckSuffix(MethodModel method, string syncName, string asyncName, List<ModelDiagnostic> diagnostics)
    {
        if (method.Name == syncName && method.IsAsync)
        {
            diagnostics.Add(new(DiagnosticCatalog.AsyncSuffix, method.Location, method.FullName, $"returns a task, so it must be named '{asyncName}'"));
        }
        else if (method.Name == asyncName && !method.IsAsync)
        {
            diagnostics.Add(new(DiagnosticCatalog.AsyncSuffix, method.Location, method.FullName, $"does not return a task, so it must be named '{syncName}'"));
        }
    }

    private static string ShortName(string typeName)
    {
        var at = typeName.LastIndexOfAny(['.', '+']);
        return at < 0 ? typeName : typeName.Substring(at + 1);
    }
}

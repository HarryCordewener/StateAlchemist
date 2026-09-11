using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>
/// Checks every state parameter against its role in its transition, for every leaf the transition can fire from:
/// a parameter must bind the same way from every leaf (spec §6.3) (SALCH0201, SALCH0202, SALCH0204 for state
/// passing, SALCH0301). A decision is checked the way it runs: its <c>Decide</c> reads the active path, which stays;
/// each <c>Complete</c> is the transform of a move to its own target; and a <c>Completed</c> runs after the move of
/// the outcome it takes — or, taking none, after every outcome's move, so it must bind the same way after each.
/// </summary>
public static class RoleValidator
{
    private enum Use
    {
        Guard,
        Transform,
        Decide,
        Action,
    }

    /// <summary>What a state parameter reads or writes.</summary>
    private enum Binding
    {
        None,
        Old,
        Live,
        Fresh,
    }

    /// <summary>Every role problem.</summary>
    public static IReadOnlyList<ModelDiagnostic> Validate(MachineModel model, Hierarchy hierarchy)
    {
        var diagnostics = new List<ModelDiagnostic>();
        foreach (var transition in model.Transitions)
        {
            var leaves = hierarchy.LeavesUnder(transition.Source).ToList();
            var paths = leaves.Select(leaf => PathPlanner.Plan(hierarchy, transition, leaf)).ToList();
            IReadOnlyList<TransitionPath> MovesTo(IEnumerable<OutcomeCompletion> completions) =>
                completions.SelectMany(c => leaves.Select(leaf => PathPlanner.Move(hierarchy, leaf, c.Target))).ToList();

            if (transition.Guard is not null)
            {
                Check(model, hierarchy, transition.Guard, Use.Guard, paths, diagnostics);
            }

            if (transition.Transform is not null)
            {
                Check(model, hierarchy, transition.Transform, Use.Transform, paths, diagnostics);
            }

            if (transition.Decision is { } decision)
            {
                if (decision.Decider is { } decider)
                {
                    Check(model, hierarchy, decider, Use.Decide, paths, diagnostics);
                }

                foreach (var completion in decision.Completions)
                {
                    Check(model, hierarchy, completion.Complete, Use.Transform, MovesTo([completion]), diagnostics);
                }
            }

            foreach (var completed in transition.Completed)
            {
                var outcome = completed.Parameters.FirstOrDefault(p => p.Kind == ParameterKind.Outcome)?.TypeName;
                var after = transition.Decision is { } made
                    ? MovesTo(made.Completions.Where(c => outcome is null || c.OutcomeType == outcome))
                    : paths;
                if (after.Count > 0)
                {
                    Check(model, hierarchy, completed, Use.Action, after, diagnostics);
                }
            }

            // A re-entry of the root is a move to the root, which never exits it: nothing is cleared.
            if (transition.Kind == MoveKind.Reenter && model.States[transition.Source].HasData && transition.Source != hierarchy.Root)
            {
                diagnostics.Add(new(DiagnosticCatalog.ReentryClearsData, transition.Location, transition.Name, model.States[transition.Source].Name));
            }
        }

        foreach (var action in model.StateActions)
        {
            foreach (var parameter in action.Method.Parameters.Where(p => p.Kind == ParameterKind.State))
            {
                if (!hierarchy.IsAncestorOrSelf(parameter.State, action.State))
                {
                    diagnostics.Add(new(DiagnosticCatalog.StateNotAvailable, action.Method.Location, parameter.Name, action.Method.FullName,
                        model.States[parameter.State].Name, $"is not '{model.States[action.State].Name}' or one of its ancestors"));
                }
                else if (parameter.Passing is Passing.Ref or Passing.Out)
                {
                    diagnostics.Add(new(DiagnosticCatalog.UnbindableParameter, action.Method.Location, parameter.Name, action.Method.FullName,
                        "an action reads states: take it by value or as in"));
                }
            }
        }

        return diagnostics;
    }

    private static void Check(
        MachineModel model,
        Hierarchy hierarchy,
        MethodModel method,
        Use use,
        IReadOnlyList<TransitionPath> paths,
        List<ModelDiagnostic> diagnostics)
    {
        var parameters = method.Parameters.Where(p => p.Kind == ParameterKind.State).ToList();
        foreach (var group in parameters.GroupBy(p => p.State).Where(g => g.Count() > 1))
        {
            var passings = group.Select(p => p.Passing).OrderBy(p => p).ToList();
            var startedOver = paths.All(path => Roles.Of(hierarchy, path, group.Key).HasFlag(Role.Exiting | Role.Entering));
            if (!(use == Use.Transform && passings.SequenceEqual([Passing.In, Passing.Ref]) && startedOver))
            {
                diagnostics.Add(new(DiagnosticCatalog.UnbindableParameter, method.Location, group.Last().Name, method.FullName,
                    $"'{model.States[group.Key].Name}' appears more than once"));
            }
        }

        foreach (var parameter in parameters)
        {
            var stateName = model.States[parameter.State].Name;
            var roles = paths.Select(path => Roles.Of(hierarchy, path, parameter.State)).ToList();
            if (roles.All(role => role == Role.None))
            {
                diagnostics.Add(new(DiagnosticCatalog.StateNotAvailable, method.Location, parameter.Name, method.FullName, stateName, "has no role in this transition"));
                continue;
            }

            var passingProblem = (use, parameter.Passing) switch
            {
                (Use.Guard, not Passing.In) => "a guard only reads states: take it as in",
                (Use.Decide, Passing.Ref or Passing.Out) => "a decision reads states: take it by value or as in",
                (Use.Transform, Passing.Value or Passing.Out) => "take a state as in or ref",
                (Use.Action, Passing.Ref or Passing.Out) => "an action reads states: take it by value or as in",
                _ => null,
            };
            if (passingProblem is not null)
            {
                diagnostics.Add(new(DiagnosticCatalog.UnbindableParameter, method.Location, parameter.Name, method.FullName, passingProblem));
                continue;
            }

            if (use == Use.Transform && parameter.Passing == Passing.Ref && roles.Any(role => role == Role.Exiting))
            {
                // At the parameter, where the fix applies, when the front-end knows where it is.
                var at = parameter.Location ?? method.Location;
                diagnostics.Add(new(DiagnosticCatalog.RefOnExitingState, at, parameter.Name, method.FullName, stateName));
                continue;
            }

            var bindings = roles.Select(role => Bind(role, parameter.Passing)).Distinct().ToList();
            if (bindings.Contains(Binding.None))
            {
                diagnostics.Add(new(DiagnosticCatalog.StateNotAvailable, method.Location, parameter.Name, method.FullName, stateName, "is not touched from every leaf this transition fires from"));
                continue;
            }

            if (bindings.Count > 1)
            {
                diagnostics.Add(new(DiagnosticCatalog.StateNotAvailable, method.Location, parameter.Name, method.FullName, stateName, "binds differently depending on the active leaf"));
                continue;
            }

            if (use == Use.Guard && bindings[0] == Binding.Fresh)
            {
                diagnostics.Add(new(DiagnosticCatalog.StateNotAvailable, method.Location, parameter.Name, method.FullName, stateName, "is not active when the guard runs"));
            }
        }
    }

    /// <summary>
    /// What a parameter binds to, given the state's role: <c>ref</c> writes the state as it will be (entering, or
    /// staying); <c>in</c> reads it as it was (exiting, or staying) — or, for a state only being entered, its fresh value.
    /// </summary>
    private static Binding Bind(Role role, Passing passing) => passing switch
    {
        Passing.Ref when role.HasFlag(Role.Entering) => Binding.Fresh,
        Passing.Ref when role.HasFlag(Role.Staying) => Binding.Live,
        Passing.Ref => Binding.None,
        _ when role.HasFlag(Role.Exiting) => Binding.Old,
        _ when role.HasFlag(Role.Staying) => Binding.Live,
        _ when role.HasFlag(Role.Entering) => Binding.Fresh,
        _ => Binding.None,
    };
}

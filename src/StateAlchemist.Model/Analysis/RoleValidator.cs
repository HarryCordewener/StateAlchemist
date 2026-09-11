using System;
using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>
/// Checks every state parameter against its role in its transition, for every leaf the transition can fire from:
/// a parameter must bind the same way from every leaf (spec §6.3) (SALCH0201, SALCH0202, SALCH0204 for state
/// passing, SALCH0301).
/// </summary>
public static class RoleValidator
{
    private enum Use
    {
        Guard,
        Transform,
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
            Func<int, TransitionPath> plan = leaf => PathPlanner.Plan(hierarchy, transition, leaf);

            if (transition.Guard is not null)
            {
                Check(model, hierarchy, transition.Guard, Use.Guard, leaves, plan, diagnostics);
            }

            if (transition.Transform is not null)
            {
                Check(model, hierarchy, transition.Transform, Use.Transform, leaves, plan, diagnostics);
            }

            foreach (var completed in transition.Completed)
            {
                Check(model, hierarchy, completed, Use.Action, leaves, plan, diagnostics);
            }

            foreach (var completion in transition.Decision?.Completions ?? [])
            {
                Check(model, hierarchy, completion.Complete, Use.Transform, leaves, leaf => PathPlanner.Move(hierarchy, leaf, completion.Target), diagnostics);
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
        IReadOnlyList<int> leaves,
        Func<int, TransitionPath> plan,
        List<ModelDiagnostic> diagnostics)
    {
        var parameters = method.Parameters.Where(p => p.Kind == ParameterKind.State).ToList();
        foreach (var group in parameters.GroupBy(p => p.State).Where(g => g.Count() > 1))
        {
            var passings = group.Select(p => p.Passing).OrderBy(p => p).ToList();
            var startedOver = leaves.All(leaf => Roles.Of(hierarchy, plan(leaf), group.Key).HasFlag(Role.Exiting | Role.Entering));
            if (!(use == Use.Transform && passings.SequenceEqual([Passing.In, Passing.Ref]) && startedOver))
            {
                diagnostics.Add(new(DiagnosticCatalog.UnbindableParameter, method.Location, group.Last().Name, method.FullName,
                    $"'{model.States[group.Key].Name}' appears more than once"));
            }
        }

        foreach (var parameter in parameters)
        {
            var stateName = model.States[parameter.State].Name;
            var roles = leaves.Select(leaf => Roles.Of(hierarchy, plan(leaf), parameter.State)).ToList();
            if (roles.All(role => role == Role.None))
            {
                diagnostics.Add(new(DiagnosticCatalog.StateNotAvailable, method.Location, parameter.Name, method.FullName, stateName, "has no role in this transition"));
                continue;
            }

            var passingProblem = (use, parameter.Passing) switch
            {
                (Use.Guard, not Passing.In) => "a guard only reads states: take it as in",
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
                diagnostics.Add(new(DiagnosticCatalog.RefOnExitingState, method.Location, parameter.Name, method.FullName, stateName));
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

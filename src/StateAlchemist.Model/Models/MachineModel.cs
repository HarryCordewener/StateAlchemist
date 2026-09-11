using System;
using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>
/// A machine as the analysis sees it: states, transitions, state actions and options, identified by index and
/// full type name. Front-ends (reflection, Roslyn) build it; validators and planners read it.
/// </summary>
public sealed class MachineModel
{
    /// <summary>Creates a model.</summary>
    /// <exception cref="ArgumentException">A state or transition is out of position.</exception>
    public MachineModel(
        string name,
        MachineOptions options,
        IReadOnlyList<StateModel> states,
        IReadOnlyList<TransitionModel> transitions,
        IReadOnlyList<StateActionModel>? stateActions = null,
        IReadOnlyList<ModelDiagnostic>? frontEndDiagnostics = null)
    {
        for (var i = 0; i < states.Count; i++)
        {
            if (states[i].Index != i)
            {
                throw new ArgumentException($"State '{states[i].Name}' is at position {i} but has index {states[i].Index}.", nameof(states));
            }
        }

        for (var i = 0; i < transitions.Count; i++)
        {
            if (transitions[i].Index != i)
            {
                throw new ArgumentException($"Transition '{transitions[i].Name}' is at position {i} but has index {transitions[i].Index}.", nameof(transitions));
            }
        }

        Name = name;
        Options = options;
        States = states;
        Transitions = transitions;
        StateActions = stateActions ?? [];
        FrontEndDiagnostics = frontEndDiagnostics ?? [];
    }

    /// <summary>The machine's name.</summary>
    public string Name { get; }

    /// <summary>Its options.</summary>
    public MachineOptions Options { get; }

    /// <summary>Its states, by index.</summary>
    public IReadOnlyList<StateModel> States { get; }

    /// <summary>Its transitions, by index.</summary>
    public IReadOnlyList<TransitionModel> Transitions { get; }

    /// <summary>Its <c>[Exited]</c> and <c>[Entered]</c> actions.</summary>
    public IReadOnlyList<StateActionModel> StateActions { get; }

    /// <summary>Problems the front-end found while building the model.</summary>
    public IReadOnlyList<ModelDiagnostic> FrontEndDiagnostics { get; }

    /// <summary>Every declared method.</summary>
    public IEnumerable<MethodModel> AllMethods =>
        Transitions.SelectMany(t => t.Methods).Concat(StateActions.Select(a => a.Method)).Distinct();

    /// <summary>The index of the state named <paramref name="typeName"/>, or −1.</summary>
    public int IndexOf(string typeName)
    {
        for (var i = 0; i < States.Count; i++)
        {
            if (States[i].TypeName == typeName)
            {
                return i;
            }
        }

        return -1;
    }
}

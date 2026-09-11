using System;
using System.Collections.Generic;

namespace StateAlchemist;

/// <summary>
/// A machine described as data: its states, their hierarchy, and its transitions. Generated machines expose theirs
/// as a static; tests and diagram tools read it instead of reflecting over the machine.
/// </summary>
public sealed class MachineDefinition
{
    /// <summary>Creates a definition and checks its invariants.</summary>
    /// <param name="valueType">The value-trigger type.</param>
    /// <param name="states">The states; <c>states[i].Index == i</c>, exactly one root, parents in range.</param>
    /// <param name="transitions">The transitions; <c>transitions[i].Index == i</c>, sources and targets in range.</param>
    /// <exception cref="ArgumentException">An invariant does not hold; the message names it.</exception>
    public MachineDefinition(Type valueType, IReadOnlyList<StateDefinition> states, IReadOnlyList<TransitionDefinition> transitions)
    {
        ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType));
        States = states ?? throw new ArgumentNullException(nameof(states));
        Transitions = transitions ?? throw new ArgumentNullException(nameof(transitions));

        StateDefinition? root = null;
        for (var i = 0; i < states.Count; i++)
        {
            var state = states[i];
            if (state.Index != i)
            {
                throw new ArgumentException($"State '{state.Name}' is at position {i} but has index {state.Index}.", nameof(states));
            }

            if (state.IsRoot)
            {
                if (root is not null)
                {
                    throw new ArgumentException($"States '{root.Name}' and '{state.Name}' are both roots.", nameof(states));
                }

                root = state;
            }
            else if (state.Parent >= states.Count)
            {
                throw new ArgumentException($"State '{state.Name}' has parent {state.Parent}, which does not exist.", nameof(states));
            }
        }

        Root = root ?? throw new ArgumentException("A machine needs exactly one root state.", nameof(states));

        for (var i = 0; i < transitions.Count; i++)
        {
            var transition = transitions[i];
            if (transition.Index != i)
            {
                throw new ArgumentException($"Transition '{transition.Name}' is at position {i} but has index {transition.Index}.", nameof(transitions));
            }

            if (transition.Source < 0 || transition.Source >= states.Count || transition.Target >= states.Count || transition.Target < -1)
            {
                throw new ArgumentException($"Transition '{transition.Name}' names a state that does not exist.", nameof(transitions));
            }
        }
    }

    /// <summary>The value-trigger type.</summary>
    public Type ValueType { get; }

    /// <summary>The states, indexed by <see cref="StateDefinition.Index"/>.</summary>
    public IReadOnlyList<StateDefinition> States { get; }

    /// <summary>The transitions, indexed by <see cref="TransitionDefinition.Index"/>.</summary>
    public IReadOnlyList<TransitionDefinition> Transitions { get; }

    /// <summary>The root state.</summary>
    public StateDefinition Root { get; }

    /// <summary>The index of <paramref name="stateType"/>, or −1 if it is not in this machine. A linear search: not for hot paths.</summary>
    /// <param name="stateType">A state struct.</param>
    public int IndexOf(Type stateType)
    {
        for (var i = 0; i < States.Count; i++)
        {
            if (States[i].Type == stateType)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The children of <paramref name="state"/>, in index order.</summary>
    /// <param name="state">A state in this definition.</param>
    public IReadOnlyList<StateDefinition> ChildrenOf(StateDefinition state)
    {
        var children = new List<StateDefinition>();
        foreach (var candidate in States)
        {
            if (candidate.Parent == state.Index)
            {
                children.Add(candidate);
            }
        }

        return children;
    }
}

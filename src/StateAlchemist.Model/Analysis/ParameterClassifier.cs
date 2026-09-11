using System;
using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>What a front-end knows about a parameter's type.</summary>
/// <param name="FullName">The type's full name.</param>
/// <param name="IsEvent">Whether it implements <c>IEvent</c>.</param>
/// <param name="SpanOf">For <c>ReadOnlySpan&lt;T&gt;</c>, <c>T</c>'s full name.</param>
/// <param name="MemoryOf">For <c>ReadOnlyMemory&lt;T&gt;</c>, <c>T</c>'s full name.</param>
/// <param name="IsCancellationToken">Whether it is <c>CancellationToken</c>.</param>
/// <param name="TransitionInfoOf">For <c>TransitionInfo&lt;T&gt;</c>, <c>T</c>'s full name.</param>
public readonly record struct TypeFacts(
    string FullName,
    bool IsEvent = false,
    string? SpanOf = null,
    string? MemoryOf = null,
    bool IsCancellationToken = false,
    string? TransitionInfoOf = null);

/// <summary>Decides what a parameter binds to, the same way for every front-end.</summary>
public static class ParameterClassifier
{
    /// <summary>The parameter's kind; for a state, <paramref name="state"/> is its index, otherwise −1.</summary>
    /// <param name="type">What the front-end knows about the parameter's type.</param>
    /// <param name="options">The machine's options.</param>
    /// <param name="stateIndex">The index of a state by full name, or −1.</param>
    /// <param name="outcomes">For a decision's methods, its outcome case types; otherwise empty.</param>
    /// <param name="state">The state's index, for <see cref="ParameterKind.State"/>.</param>
    public static ParameterKind Classify(TypeFacts type, MachineOptions options, Func<string, int> stateIndex, IReadOnlyCollection<string> outcomes, out int state)
    {
        state = stateIndex(type.FullName);
        if (state >= 0)
        {
            return ParameterKind.State;
        }

        if (type.IsCancellationToken)
        {
            return ParameterKind.CancellationToken;
        }

        if (type.SpanOf is not null && type.SpanOf == options.ValueType)
        {
            return ParameterKind.Run;
        }

        if (type.MemoryOf is not null && type.MemoryOf == options.ValueType)
        {
            return ParameterKind.RunMemory;
        }

        if (type.TransitionInfoOf is not null && type.TransitionInfoOf == options.ValueType)
        {
            return ParameterKind.TransitionInfo;
        }

        if (type.FullName == options.ValueType)
        {
            return ParameterKind.Value;
        }

        if (type.FullName == options.ContextType)
        {
            return ParameterKind.Context;
        }

        if (type.FullName == options.ConfigType)
        {
            return ParameterKind.Config;
        }

        if (outcomes.Contains(type.FullName))
        {
            return ParameterKind.Outcome;
        }

        return type.IsEvent ? ParameterKind.Event : ParameterKind.Unknown;
    }
}

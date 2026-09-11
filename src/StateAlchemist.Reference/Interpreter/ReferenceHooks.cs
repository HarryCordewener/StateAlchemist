using System;

namespace StateAlchemist.Reference;

/// <summary>
/// The reference interpreter's hooks: what a generated machine's <c>partial</c> hook methods are. A hook left
/// <see langword="null"/> is a hook not implemented.
/// </summary>
/// <typeparam name="TValue">The value-trigger type.</typeparam>
public sealed class ReferenceHooks<TValue>
    where TValue : struct
{
    /// <summary>Called after a transition is observed.</summary>
    public delegate void TransitionedHook(in TransitionInfo<TValue> transition);

    /// <summary>Called when a phase throws; sets the resolution.</summary>
    public delegate void ExceptionHook(Exception exception, in TransitionInfo<TValue> transition, ref ExceptionResolution resolution);

    /// <summary>After every transition: <c>OnTransitioned</c>.</summary>
    public TransitionedHook? Transitioned { get; set; }

    /// <summary>A value nothing handled: <c>OnUnhandled(StateId, TValue)</c>.</summary>
    public Action<Type, TValue>? UnhandledValue { get; set; }

    /// <summary>An event nothing handled: <c>OnUnhandled(StateId, in TEvent)</c>.</summary>
    public Action<Type, object>? UnhandledEvent { get; set; }

    /// <summary>A phase threw: <c>On{Phase}Exception</c>. The phase is <see cref="TransitionInfo{TValue}.Phase"/>.</summary>
    public ExceptionHook? Exception { get; set; }
}

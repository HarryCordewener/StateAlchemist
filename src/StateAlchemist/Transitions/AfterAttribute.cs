using System;

namespace StateAlchemist;

/// <summary>
/// Fires the transition when its <c>From</c> state has been active for a while: a timer armed when the state is
/// entered and cancelled when it is exited. Set <see cref="Milliseconds"/> or <see cref="Seconds"/>, or leave both
/// unset on a class-form transition that declares a <c>Delay</c> method, which computes the delay when the state is
/// entered.
/// </summary>
/// <remarks>
/// A machine with a timer takes a <c>TimeProvider</c> as the last argument of its constructor, defaulting to the
/// system clock. The timer's firing goes through the machine's inbox like any other input.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AfterAttribute : Attribute
{
    /// <summary>The delay in milliseconds. Zero means not set.</summary>
    public int Milliseconds { get; set; }

    /// <summary>The delay in seconds. Zero means not set.</summary>
    public int Seconds { get; set; }
}

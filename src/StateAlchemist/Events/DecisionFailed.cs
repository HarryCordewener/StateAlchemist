using System;

namespace StateAlchemist;

/// <summary>
/// Fired when a decision's <c>Decide</c> or <c>DecideAsync</c> throws. The decision is over: nothing it would have
/// changed has changed, and the machine is back in the state it decided from. A transition on this event — from the
/// decision's source or any ancestor — is the recovery; unhandled, it is an unhandled trigger like any other.
/// </summary>
/// <remarks>
/// A runtime type rather than a generated one, so that a module can declare
/// <c>[Transition(From = typeof(AuthRequested), To = typeof(Idle)), OnEvent(typeof(DecisionFailed))]</c> before any
/// machine includes it.
/// </remarks>
public readonly struct DecisionFailed : IEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="decision">The decision that failed, such as <c>AuthModule.CheckAuth</c>.</param>
    /// <param name="exception">What it threw.</param>
    public DecisionFailed(string decision, Exception exception)
    {
        Decision = decision;
        Exception = exception;
    }

    /// <summary>The decision that failed, such as <c>AuthModule.CheckAuth</c>: a guard can tell two decisions apart.</summary>
    public string Decision { get; }

    /// <summary>What it threw.</summary>
    public Exception Exception { get; }

    /// <summary>For logs: <c>AuthModule.CheckAuth failed: the service is unavailable</c>.</summary>
    public override string ToString() => $"{Decision} failed: {Exception.Message}";
}

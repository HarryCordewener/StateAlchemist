using System;

namespace StateAlchemist;

/// <summary>A machine declared <see cref="Unhandled.Throw"/> received a trigger nothing handles.</summary>
public sealed class UnhandledTriggerException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="state">The active leaf.</param>
    /// <param name="trigger">The trigger, as text.</param>
    public UnhandledTriggerException(Type state, string trigger)
        : base($"No transition handles {trigger} in state '{state.Name}'.")
    {
        State = state;
        Trigger = trigger;
    }

    /// <summary>The active leaf.</summary>
    public Type State { get; }

    /// <summary>The trigger, as text.</summary>
    public string Trigger { get; }
}

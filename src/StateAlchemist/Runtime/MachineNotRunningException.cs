using System;

namespace StateAlchemist;

/// <summary>A machine was fired before <c>StartAsync</c> completed, or after it stopped.</summary>
public sealed class MachineNotRunningException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="status">The machine's status: <see cref="MachineStatus.NotStarted"/> or <see cref="MachineStatus.Stopped"/>.</param>
    public MachineNotRunningException(MachineStatus status)
        : base(status == MachineStatus.NotStarted
            ? "The machine has not been started. Call StartAsync before firing it."
            : "The machine has been stopped and cannot be fired.")
    {
        Status = status;
    }

    /// <summary>The machine's status when it was fired.</summary>
    public MachineStatus Status { get; }
}

using System;

namespace StateAlchemist;

/// <summary>A machine was fired before <c>StartAsync</c> completed, or after it stopped.</summary>
public sealed class MachineNotRunningException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="status">The machine's status: anything but <see cref="MachineStatus.Running"/>.</param>
    public MachineNotRunningException(MachineStatus status)
        : base(status switch
        {
            MachineStatus.NotStarted => "The machine has not been started. Call StartAsync before firing it.",
            MachineStatus.Completed => "The machine has completed: it entered a final state and cannot be fired.",
            MachineStatus.Paused => "The machine is paused. Call Resume before firing it.",
            _ => "The machine has been stopped and cannot be fired.",
        })
    {
        Status = status;
    }

    /// <summary>The machine's status when it was fired.</summary>
    public MachineStatus Status { get; }
}

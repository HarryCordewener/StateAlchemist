namespace StateAlchemist;

/// <summary>Where a machine is in its lifecycle (spec §7, decision D22).</summary>
public enum MachineStatus
{
    /// <summary>Constructed; <c>StartAsync</c> has not completed. Firing throws <see cref="MachineNotRunningException"/>.</summary>
    NotStarted,

    /// <summary>Started and accepting triggers.</summary>
    Running,

    /// <summary>Stopped by <c>StopAsync</c> or disposal. Firing throws <see cref="MachineNotRunningException"/>.</summary>
    Stopped,
}

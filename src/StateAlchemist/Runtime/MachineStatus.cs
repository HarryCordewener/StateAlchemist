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

    /// <summary>
    /// Entered a <see cref="FinalAttribute"/> child of the root. It refuses triggers, as a stopped machine does, but has
    /// not run its <c>[Exited]</c> actions: <c>StopAsync</c> runs them and stops it.
    /// </summary>
    Completed,

    /// <summary>
    /// Paused by <see cref="IStatechart{TValue}.Pause"/>: it refuses triggers and its timers are held, until
    /// <see cref="IStatechart{TValue}.Resume"/>.
    /// </summary>
    Paused,
}

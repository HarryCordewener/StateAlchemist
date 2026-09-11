namespace StateAlchemist;

/// <summary>What an exception hook tells the machine to do about an exception (spec §6.9).</summary>
public enum ExceptionResolution
{
    /// <summary>The default: before the state changes, nothing commits; after, the remaining actions are skipped. The exception propagates.</summary>
    Rethrow,

    /// <summary>Swallow it. A guard counts as false; a transform drops the trigger; an action skips the remaining actions.</summary>
    Skip,

    /// <summary>After the state has changed only: swallow it and run the remaining actions. Before, it acts as <see cref="Skip"/>.</summary>
    Continue,
}

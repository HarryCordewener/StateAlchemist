namespace StateAlchemist;

/// <summary>How a machine guards against being fired by more than one caller at once (spec §6.10).</summary>
public enum Concurrency
{
    /// <summary>One caller at a time; overlapping calls throw <c>ConcurrentUseException</c> and never corrupt state.</summary>
    Checked,

    /// <summary>One caller at a time, promised by the host and not checked. Fastest; misuse corrupts state.</summary>
    Unchecked,

    /// <summary>Any thread may fire. Calls queue in a channel and are processed one at a time, each to completion.</summary>
    Serialized,
}

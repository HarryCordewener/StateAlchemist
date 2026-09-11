using System;

namespace StateAlchemist;

/// <summary>A <see cref="Concurrency.Checked"/> machine was fired by two callers at once. Its state is unchanged by the second call.</summary>
public sealed class ConcurrentUseException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public ConcurrentUseException()
        : base("The machine was fired by two callers at once. Serialise calls, or declare the machine Concurrency.Serialized.")
    {
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StateAlchemist.Samples.Door;

// A door with a card reader: the example for decisions. Something outside the machine — a directory, a database —
// decides what a badge means, and it may take a while. The machine parks in a pending state while it waits, the
// caller's FireAsync waits with it, and an event can end the wait.

/// <summary>The outside world the door asks.</summary>
public sealed class Access
{
    /// <summary>What the reader logged, in order.</summary>
    public List<string> Log { get; } = [];

    /// <summary>Answers a badge; a real one would be a network call.</summary>
    public Func<int, CancellationToken, ValueTask<bool>> Check { get; set; } = (_, _) => new ValueTask<bool>(true);
}

/// <summary>A badge was presented.</summary>
public readonly struct Badge(int number) : IEvent
{
    /// <summary>Which badge.</summary>
    public int Number { get; } = number;
}

/// <summary>The wait is over, whatever the reader thinks.</summary>
public readonly struct GaveUp : IEvent;

// begin-snippet: sample-door-outcomes
/// <summary>What the reader can decide. Each case carries what its outcome needs.</summary>
public readonly record struct Allowed(string Name);

/// <summary>The badge is not allowed in.</summary>
public readonly record struct Refused(string Reason);

/// <summary>The decision's result: one of these, and the generator writes a <c>Complete</c> for each.</summary>
public union Answer(Allowed, Refused);
// end-snippet

/// <summary>The door itself.</summary>
public struct DoorFrame : IRootState
{
    /// <summary>How many people have been let in.</summary>
    public int Admitted;
}

/// <summary>Shut, waiting for a badge.</summary>
[Initial]
public struct Locked : IState<DoorFrame>
{
}

/// <summary>Open.</summary>
public struct Unlocked : IState<DoorFrame>
{
    /// <summary>Who opened it.</summary>
    public string? Name;
}

// begin-snippet: sample-door-decision
[Module]
public static class DoorModule
{
    /// <summary>
    /// A decision: the machine moves into a pending state below <see cref="Locked"/>, the caller's
    /// <c>FireAsync</c> keeps waiting, and the outcome picks the transition that follows.
    /// </summary>
    [Decision(From = typeof(Locked), Handle = new[] { typeof(GaveUp) }), OnEvent(typeof(Badge))]
    public static class Read
    {
        public static async ValueTask<Answer> DecideAsync(Access access, Badge badge, CancellationToken cancellation) =>
            await access.Check(badge.Number, cancellation).ConfigureAwait(false)
                ? new Answer(new Allowed($"badge {badge.Number}"))
                : new Answer(new Refused("unknown badge"));

        /// <summary>One <c>Complete</c> per outcome, each naming where that outcome goes.</summary>
        [To(typeof(Unlocked))]
        public static void Complete(ref DoorFrame door, ref Unlocked unlocked, Allowed outcome)
        {
            door.Admitted++;
            unlocked.Name = outcome.Name;
        }

        [To(typeof(Locked))]
        public static void Complete(Refused outcome)
        {
        }

        /// <summary>And an action per outcome, after the state has changed.</summary>
        public static void Completed(Access access, Allowed outcome) => access.Log.Add($"opened for {outcome.Name}");

        public static void Completed(Access access, Refused outcome) => access.Log.Add($"refused: {outcome.Reason}");
    }

    /// <summary>
    /// <c>Handle</c> above lets this run while the decision is pending, and what ends the wait is *leaving* the
    /// pending state: this names <c>Locked</c> as its target, so it is a transition out of the pending state and
    /// the reader's <c>CancellationToken</c> is cancelled. A stay would keep waiting.
    /// </summary>
    [Transition(From = typeof(Locked), To = typeof(Locked)), OnEvent(typeof(GaveUp))]
    public static class Abandon
    {
        public static void Transform()
        {
        }

        public static void Completed(Access access) => access.Log.Add("gave up waiting");
    }

    /// <summary>A reader that throws fires <see cref="DecisionFailed"/>, which is an ordinary trigger to recover from.</summary>
    [Transition(From = typeof(Locked)), OnEvent(typeof(DecisionFailed))]
    public static class Broken
    {
        public static void Transform()
        {
        }

        public static void Completed(Access access, DecisionFailed failure) =>
            access.Log.Add($"reader failed: {failure.Exception.Message}");
    }

    [Transition(From = typeof(Unlocked), To = typeof(Locked)), OnEvent(typeof(GaveUp))]
    public static void Close()
    {
    }

    /// <summary>This door is driven by events; a machine still says what its values do, and here they do nothing.</summary>
    [Transition(From = typeof(DoorFrame)), OnAny]
    public static void Ignore()
    {
    }
}
// end-snippet

[Machine(Root = typeof(DoorFrame), Value = typeof(byte), Context = typeof(Access))]
[Include(typeof(DoorModule))]
public sealed partial class CardDoor;

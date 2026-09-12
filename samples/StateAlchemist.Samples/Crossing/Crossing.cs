using System.Collections.Generic;

namespace StateAlchemist.Samples.Crossing;

// A pedestrian crossing: the example for the things a flat machine cannot show — a tree of states, data whose
// lifetime is its place in that tree, two transitions competing for one trigger, and a move across two levels.

/// <summary>What the machine talks to.</summary>
public sealed class CrossingLog
{
    /// <summary>What happened, in order.</summary>
    public List<string> Entries { get; } = [];
}

// begin-snippet: sample-crossing-triggers
/// <summary>What can happen to a crossing.</summary>
public enum Signal : byte
{
    /// <summary>A second of the cycle.</summary>
    Tick = 1,

    /// <summary>Someone pressed the button.</summary>
    Requested,

    /// <summary>The controller reports a fault.</summary>
    Fault,

    /// <summary>An engineer clears it.</summary>
    Cleared,
}
// end-snippet

// begin-snippet: sample-crossing-states
/// <summary>The junction. Its data lives as long as the machine.</summary>
public struct Junction : IRootState
{
    /// <summary>Completed light cycles since the machine started.</summary>
    public int Cycles;
}

/// <summary>Signalling normally. A pending request belongs here: both lights need it, neither owns it.</summary>
[Initial]
public struct Working : IState<Junction>
{
    /// <summary>Whether someone is waiting to cross.</summary>
    public bool Requested;
}

/// <summary>Traffic stopped. Where a working crossing starts.</summary>
[Initial]
public struct Red : IState<Working>
{
}

/// <summary>Traffic moving. What it counts dies when the light changes.</summary>
public struct Green : IState<Working>
{
    /// <summary>Seconds this green has lasted.</summary>
    public int Seconds;
}

/// <summary>About to stop.</summary>
public struct Amber : IState<Working>
{
}

/// <summary>Out of service.</summary>
public struct Faulted : IState<Junction>
{
}

/// <summary>What a faulted crossing does. Every state with children names one to enter first.</summary>
[Initial]
public struct Flashing : IState<Faulted>
{
}
// end-snippet

// begin-snippet: sample-crossing-module
[Module]
public static class CrossingModule
{
    [Transition(From = typeof(Red), To = typeof(Green)), On(Signal.Tick)]
    public static void Go(ref Green green) => green.Seconds = 0;

    /// <summary>
    /// Two transitions want <c>Tick</c> in <see cref="Green"/>. The guarded one is tried first, by
    /// <c>Order</c>; the unguarded one is the fallback.
    /// </summary>
    [Transition(From = typeof(Green), To = typeof(Amber), Order = 1), On(Signal.Tick)]
    public static class Change
    {
        /// <summary>Change early for someone waiting, but not before the green has run for three seconds.</summary>
        public static bool Guard(in Green green, in Working working) => working.Requested && green.Seconds >= 3;

        public static void Transform()
        {
        }
    }

    [Transition(From = typeof(Green)), On(Signal.Tick)]
    public static void Count(ref Green green) => green.Seconds++;

    /// <summary>
    /// Amber to red crosses no boundary the button cares about: <see cref="Working"/> stays, so the request it
    /// holds can be cleared here, and the cycle counted on the root.
    /// </summary>
    [Transition(From = typeof(Amber), To = typeof(Red)), On(Signal.Tick)]
    public static void Stop(ref Working working, ref Junction junction)
    {
        working.Requested = false;
        junction.Cycles++;
    }

    /// <summary>The button, wherever the crossing is working. The flag lives above both lights, so it survives.</summary>
    [Transition(From = typeof(Working)), On(Signal.Requested)]
    public static void Request(ref Working working) => working.Requested = true;

    /// <summary>A fault moves two levels: out of the light and out of <see cref="Working"/>, into the flashing state.</summary>
    [Transition(From = typeof(Working), To = typeof(Faulted)), On(Signal.Fault)]
    public static void Fail()
    {
    }

    /// <summary>And back. Entering <see cref="Working"/> continues to its <c>[Initial]</c> child, so it starts at red.</summary>
    [Transition(From = typeof(Faulted), To = typeof(Working)), On(Signal.Cleared)]
    public static void Clear()
    {
    }

    /// <summary>Anything a state does not handle.</summary>
    [Transition(From = typeof(Junction)), OnAny]
    public static void Ignore()
    {
    }

    [Entered(typeof(Green))]
    public static void Started(CrossingLog log) => log.Entries.Add("green");

    /// <summary>The state being left is still readable here; it is cleared immediately afterwards.</summary>
    [Exited(typeof(Green))]
    public static void Ended(CrossingLog log, Green green) => log.Entries.Add($"green lasted {green.Seconds}s");

    [Entered(typeof(Flashing))]
    public static void Broke(CrossingLog log) => log.Entries.Add("flashing");
}
// end-snippet

// begin-snippet: sample-crossing-machine
[Machine(Root = typeof(Junction), Value = typeof(Signal), Context = typeof(CrossingLog))]
[Include(typeof(CrossingModule))]
public sealed partial class PedestrianCrossing;
// end-snippet

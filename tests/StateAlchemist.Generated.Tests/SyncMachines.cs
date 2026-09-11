using System;
using StateAlchemist.Samples.Performance;

namespace StateAlchemist.Generated.Tests;

// Machines with nothing async anywhere: the case the synchronous Fire exists for. They reuse the performance
// sample's states, with modules of their own, because a state type belongs to no module — only transitions do.

[Module]
public static class SyncModule
{
    /// <summary>1: a stay that counts.</summary>
    [Transition(From = typeof(Text)), On(1)]
    public static void Count(ref Text self) => self.Count++;

    /// <summary>Anything else in Text is text, taken as a run.</summary>
    [Transition(From = typeof(Text)), OnAny, Run]
    public static void Capture(ref Text self, ReadOnlySpan<byte> run) => self.Length += run.Length;

    /// <summary>A tick moves the root's counter, wherever it arrives.</summary>
    [Transition(From = typeof(Root)), OnEvent(typeof(Tick))]
    public static void Ticked(ref Root root) => root.Moves++;
}

[Module]
public static class SyncThrowingModule
{
    /// <summary>7: a transform that throws, so a caller of <c>Fire</c> can see what it gets.</summary>
    [Transition(From = typeof(Text)), On(7)]
    public static void Throw(ref Text self) => throw new InvalidOperationException("no");

    [Transition(From = typeof(Text)), OnAny]
    public static void Ignore()
    {
    }
}

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters))]
[Include(typeof(SyncModule))]
public sealed partial class SyncMachine;

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters))]
[Include(typeof(SyncThrowingModule))]
public sealed partial class SyncThrowingMachine;

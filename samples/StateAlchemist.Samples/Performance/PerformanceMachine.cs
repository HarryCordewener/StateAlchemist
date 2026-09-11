using System;
using System.Threading;
using System.Threading.Tasks;

namespace StateAlchemist.Samples.Performance;

// The shapes spec §9 measures: a stay, a move across two levels with synchronous actions, a run over a 1 KB payload,
// and an action that suspends. Every action counts instead of logging, so what a benchmark or an allocation test
// sees is the machine's own cost.
//
// Root ─┬─ Outer [Initial] ─── Text [Initial]
//       └─ Other ─────────── Parked [Initial]

public sealed class Counters
{
    public int Exits;
    public int Entries;
    public int Completions;
    public int Suspensions;
}

public struct Root : IRootState
{
    public int Moves;
}

[Initial]
public struct Outer : IState<Root>
{
}

[Initial]
public struct Text : IState<Outer>
{
    public int Count;
    public int Length;
}

public struct Other : IState<Root>
{
}

[Initial]
public struct Parked : IState<Other>
{
}

/// <summary>A tick, for the typed-event path.</summary>
public readonly struct Tick : IEvent;

[Module]
public static class PerformanceModule
{
    public const byte Iac = 255;

    /// <summary>1: a stay with a synchronous transform.</summary>
    [Transition(From = typeof(Text)), On(1)]
    public static void Count(ref Text self) => self.Count++;

    /// <summary>2: across two levels — exits Text and Outer, enters Other and Parked.</summary>
    [Transition(From = typeof(Text), To = typeof(Parked)), On(2)]
    public static void Park(ref Root root) => root.Moves++;

    /// <summary>3: and back.</summary>
    [Transition(From = typeof(Parked), To = typeof(Text)), On(3)]
    public static void Resume(ref Root root) => root.Moves++;

    /// <summary>Any other value in Text is text: a run, up to the next IAC or command value.</summary>
    [Transition(From = typeof(Text)), OnAny, Run]
    public static void Capture(ref Text self, ReadOnlySpan<byte> run) => self.Length += run.Length;

    /// <summary>IAC is not text: the stop value of the run.</summary>
    [Transition(From = typeof(Text)), On(Iac)]
    public static void Escape(ref Text self) => self.Count++;

    /// <summary>Anything else while parked is ignored.</summary>
    [Transition(From = typeof(Other)), OnAny]
    public static void Ignore()
    {
    }

    /// <summary>A tick counts, wherever it arrives.</summary>
    [Transition(From = typeof(Root)), OnEvent(typeof(Tick))]
    public static void Ticked(ref Root root) => root.Moves++;

    /// <summary>4: a stay whose action suspends.</summary>
    [Transition(From = typeof(Text)), On(4)]
    public static class Suspend
    {
#if NET6_0_OR_GREATER
        // The machine's own async methods are pooled; an action that suspends should be too, or the benchmark
        // measures the sample's state machine instead of the machine's.
        [global::System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(global::System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder))]
#endif
        public static async ValueTask CompletedAsync(Counters counters)
        {
            await Task.Yield();
            counters.Suspensions++;
        }
    }

    [Exited(typeof(Text))]
    public static void LeaveText(Counters counters) => counters.Exits++;

    [Exited(typeof(Outer))]
    public static void LeaveOuter(Counters counters) => counters.Exits++;

    [Entered(typeof(Other))]
    public static void EnterOther(Counters counters) => counters.Entries++;

    [Entered(typeof(Parked))]
    public static void EnterParked(Counters counters) => counters.Entries++;
}

/// <summary>An async decision, which turns a machine that includes it into one with an inbox.</summary>
[Module]
public static class PerformanceDecisionModule
{
    public readonly record struct Yes;

    public readonly record struct No;

    public union Answer(Yes, No);

    /// <summary>9: decide, then stay.</summary>
    [Decision(From = typeof(Text)), On(9)]
    public static class Ask
    {
        public static ValueTask<Answer> DecideAsync(CancellationToken cancellation) => new(new Answer(new Yes()));

        [To(typeof(Parked))]
        public static void Complete(Yes outcome)
        {
        }

        [To(typeof(Parked))]
        public static void Complete(No outcome)
        {
        }
    }
}

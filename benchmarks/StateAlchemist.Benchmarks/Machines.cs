using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using StateAlchemist.Samples.Performance;

namespace StateAlchemist.Benchmarks;

// Each machine reads back what its transitions changed, so a benchmark can consume the result of firing it.
// TryGetText is what any consumer would call: a check that the state is active, then a copy of its data.

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters))]
[Include(typeof(PerformanceModule))]
public sealed partial class InlineMachine
{
    public int Count { get { TryGetText(out var text); return text.Count; } }

    public int Length { get { TryGetText(out var text); return text.Length; } }
}

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters), Concurrency = Concurrency.Unchecked)]
[Include(typeof(PerformanceModule))]
public sealed partial class UncheckedMachine;

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters), Concurrency = Concurrency.Serialized)]
[Include(typeof(PerformanceModule))]
public sealed partial class SerializedMachine;

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters))]
[Include(typeof(PerformanceModule)), Include(typeof(PerformanceDecisionModule))]
public sealed partial class DecidingMachine;

/// <summary>A decision that suspends before it answers, then goes back to Text.</summary>
[Module]
public static class SuspendingDecisionModule
{
    public const byte Ask = 5;

    public readonly record struct Yes;

    public readonly record struct No;

    public union Answer(Yes, No);

    [Decision(From = typeof(Text)), On(Ask)]
    public static class Decide
    {
        // Pooled for the same reason as the sample's suspending action: the benchmark measures the machine.
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
        public static async ValueTask<Answer> DecideAsync(Counters counters)
        {
            await Task.Yield();
            counters.Suspensions++;
            return new Answer(new Yes());
        }

        [To(typeof(Text))]
        public static void Complete(Yes outcome)
        {
        }

        [To(typeof(Text))]
        public static void Complete(No outcome)
        {
        }
    }
}

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters))]
[Include(typeof(PerformanceModule)), Include(typeof(SuspendingDecisionModule))]
public sealed partial class SuspendingDecisionMachine;

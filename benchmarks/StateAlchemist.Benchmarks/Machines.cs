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

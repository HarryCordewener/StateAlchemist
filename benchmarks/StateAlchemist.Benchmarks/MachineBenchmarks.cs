using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using StateAlchemist.Samples.Performance;

namespace StateAlchemist.Benchmarks;

/// <summary>Spec §9, per call: a stay, a move across two levels and back, a 1 KB run, and an action that suspends.</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class MachineBenchmarks
{
    private static readonly byte[] Kilobyte = Enumerable.Repeat((byte)'a', 1024).ToArray();

    private readonly Counters _counters = new();

    private InlineMachine _generated = null!;
    private HandWrittenMachine _handWritten = null!;
    private StatelessMachine _stateless = null!;

    [GlobalSetup]
    public void Setup()
    {
        _generated = new InlineMachine(_counters);
        _generated.StartAsync().GetAwaiter().GetResult();
        _handWritten = new HandWrittenMachine(new Counters());
        _stateless = new StatelessMachine(new Counters());
    }

    // The machine is IAsyncDisposable: stopping it runs its [Exited] actions, after the measurements are in.
    [GlobalCleanup]
    public void Cleanup() => _generated.DisposeAsync().GetAwaiter().GetResult();

    // Every benchmark returns state the firing changed, so nothing measured here can be optimised away: a
    // hand-written switch whose result is unused compiles to nothing, and then there is no baseline to be within.
    [Benchmark(Baseline = true), BenchmarkCategory("Stay")]
    public int StayHandWritten()
    {
        _ = _handWritten.FireAsync(1);
        return _handWritten.Count;
    }

    [Benchmark, BenchmarkCategory("Stay")]
    public int StayGenerated()
    {
        _ = _generated.FireAsync(1);
        return _generated.Count;
    }

    [Benchmark, BenchmarkCategory("Stay")]
    public int StayStateless()
    {
        _stateless.Fire(1);
        return _stateless.Count;
    }

    [Benchmark, BenchmarkCategory("Move")]
    public int MoveAndBackHandWritten()
    {
        _ = _handWritten.FireAsync(2);
        _ = _handWritten.FireAsync(3);
        return _handWritten.Count;
    }

    [Benchmark, BenchmarkCategory("Move")]
    public int MoveAndBackGenerated()
    {
        _ = _generated.FireAsync(2);
        _ = _generated.FireAsync(3);
        return _generated.Count;
    }

    [Benchmark, BenchmarkCategory("Move")]
    public int MoveAndBackStateless()
    {
        _stateless.Fire(2);
        _stateless.Fire(3);
        return _stateless.Count;
    }

    [Benchmark, BenchmarkCategory("Run")]
    public int KilobyteRunHandWritten()
    {
        _ = _handWritten.FireAsync(Kilobyte);
        return _handWritten.Length;
    }

    [Benchmark, BenchmarkCategory("Run")]
    public int KilobyteRunGenerated()
    {
        _ = _generated.FireAsync(Kilobyte);
        return _generated.Length;
    }

    [Benchmark, BenchmarkCategory("Run")]
    public int KilobyteRunStateless()
    {
        foreach (var value in Kilobyte)
        {
            _stateless.Fire(value);
        }

        return _stateless.Length;
    }

    // What the action costs on its own is the floor for firing it: the difference is what the machine adds.
    [Benchmark(Baseline = true), BenchmarkCategory("Suspend")]
    public ValueTask SuspendingActionAlone() => PerformanceModule.Suspend.CompletedAsync(_counters);

    [Benchmark, BenchmarkCategory("Suspend")]
    public ValueTask SuspendingActionGenerated() => _generated.FireAsync(4);
}

/// <summary>What each concurrency mode costs on the same stay, and a machine whose async decision gives it an inbox.</summary>
[MemoryDiagnoser]
public class ConcurrencyBenchmarks
{
    private InlineMachine _checked = null!;
    private UncheckedMachine _unchecked = null!;
    private SerializedMachine _serialized = null!;
    private DecidingMachine _deciding = null!;

    private IMachine<byte>[] Machines => [_checked, _unchecked, _serialized, _deciding];

    [GlobalSetup]
    public void Setup()
    {
        _checked = new InlineMachine(new Counters());
        _unchecked = new UncheckedMachine(new Counters());
        _serialized = new SerializedMachine(new Counters());
        _deciding = new DecidingMachine(new Counters());
        foreach (var machine in Machines)
        {
            machine.StartAsync().GetAwaiter().GetResult();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var machine in Machines)
        {
            machine.DisposeAsync().GetAwaiter().GetResult();
        }
    }

    [Benchmark]
    public ValueTask Unchecked() => _unchecked.FireAsync(1);

    [Benchmark(Baseline = true)]
    public ValueTask Checked() => _checked.FireAsync(1);

    [Benchmark]
    public ValueTask Serialized() => _serialized.FireAsync(1);

    [Benchmark]
    public ValueTask CheckedWithAnInbox() => _deciding.FireAsync(1);
}

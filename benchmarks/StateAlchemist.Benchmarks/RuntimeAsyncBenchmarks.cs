using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.DotNetCli;
using StateAlchemist.Samples.Performance;

namespace StateAlchemist.Benchmarks;

/// <summary>
/// The suspending paths twice over: once as built today, and once with every project compiled under
/// <c>runtime-async=on</c>, the .NET 11 preview feature. Both jobs build their own copy, so this class is run out of
/// process, unlike the rest.
/// </summary>
[Config(typeof(RuntimeAsyncConfig))]
[MemoryDiagnoser]
public class RuntimeAsyncBenchmarks
{
    private readonly Counters _counters = new();

    private InlineMachine _generated = null!;
    private SuspendingDecisionMachine _deciding = null!;

    [GlobalSetup]
    public void Setup()
    {
        _generated = new InlineMachine(_counters);
        _generated.StartAsync().GetAwaiter().GetResult();
        _deciding = new SuspendingDecisionMachine(new Counters());
        _deciding.StartAsync().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _generated.DisposeAsync().GetAwaiter().GetResult();
        _deciding.DisposeAsync().GetAwaiter().GetResult();
    }

    /// <summary>The async action on its own, awaited directly.</summary>
    [Benchmark(Baseline = true)]
    public ValueTask SuspendingAction() => PerformanceModule.Suspend.CompletedAsync(_counters);

    /// <summary>A FireAsync whose action suspends.</summary>
    [Benchmark]
    public ValueTask SuspendingFireAsync() => _generated.FireAsync(4);

    /// <summary>A FireAsync whose async decision suspends, through the inbox the decision gives the machine.</summary>
    [Benchmark]
    public ValueTask SuspendingDecision() => _deciding.FireAsync(SuspendingDecisionModule.Ask);

    private sealed class RuntimeAsyncConfig : ManualConfig
    {
        public RuntimeAsyncConfig()
        {
            var job = Job.Default.WithRuntime(CoreRuntime.Core11_0);
            AddJob(job.WithId("StateMachines").AsBaseline());

            // %3D is an escaped '=': the property's value is runtime-async=on. A global property, so it reaches the
            // samples' actions as well as this project and the machines generated into it.
            AddJob(job.WithArguments([new MsBuildArgument("/p:Features=runtime-async%3Don")]).WithId("RuntimeAsync"));
        }
    }
}

using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.DotNetCli;
using StateAlchemist.Samples.Performance;

namespace StateAlchemist.Benchmarks;

/// <summary>
/// The suspending paths twice over: once with every project compiled to state machines, and once under
/// <c>runtime-async=on</c>, which the repository's net11.0 builds use. Both jobs build their own copy, so this class is
/// run out of process, unlike the rest.
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
            // Directory.Build.targets turns runtime async on for net11.0; UseRuntimeAsync=false turns it off. A global
            // property, so it reaches the samples' actions as well as this project and the machines generated into it.
            AddJob(job.WithArguments([new MsBuildArgument("/p:UseRuntimeAsync=false")]).WithId("StateMachines").AsBaseline());
            AddJob(job.WithArguments([new MsBuildArgument("/p:UseRuntimeAsync=true")]).WithId("RuntimeAsync"));
        }
    }
}

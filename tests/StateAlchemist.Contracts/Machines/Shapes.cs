using StateAlchemist.Contracts.Machines.Deciding;
using StateAlchemist.Contracts.Machines.Failures;
using StateAlchemist.Contracts.Machines.Guards;
using StateAlchemist.Contracts.Machines.Recording;
using StateAlchemist.Contracts.Machines.Runs;
using StateAlchemist.Samples.Telnet;

namespace StateAlchemist.Contracts.Machines;

/// <summary>The contract machines. A generated test project declares one <c>[Machine]</c> class per shape, with the same modules.</summary>
public static class Shapes
{
    public static readonly MachineShape Recorder = new("Recorder", typeof(Root), [typeof(RecorderModule), typeof(RecorderExtras)], typeof(RecordingContext));

    public static readonly MachineShape Guards = new("Guards", typeof(GuardRoot), [typeof(GuardModule)], typeof(RecordingContext));

    public static readonly MachineShape GuardsThatThrow = Guards with { Name = "GuardsThatThrow", Unhandled = Unhandled.Throw };

    public static readonly MachineShape Failures = new("Failures", typeof(FailRoot), [typeof(FailureModule)], typeof(RecordingContext));

    public static readonly MachineShape RecorderUnchecked = Recorder with { Name = "RecorderUnchecked", Concurrency = Concurrency.Unchecked };

    public static readonly MachineShape RecorderSerialized = Recorder with { Name = "RecorderSerialized", Concurrency = Concurrency.Serialized };

    public static readonly MachineShape Deciding = new("Deciding", typeof(DecideRoot), [typeof(DecidingModule)], typeof(RecordingContext));

    public static readonly MachineShape DecidingSerialized = Deciding with { Name = "DecidingSerialized", Concurrency = Concurrency.Serialized };

    public static readonly MachineShape Runs = new("Runs", typeof(RunRoot), [typeof(RunModule)], typeof(RecordingContext));

    public static readonly MachineShape RunsSerialized = Runs with { Name = "RunsSerialized", Concurrency = Concurrency.Serialized };

    public static readonly MachineShape Telnet = new("SampleTelnet", typeof(Connected), [typeof(TelnetCore), typeof(GmcpModule), typeof(NawsModule)], typeof(TelnetContext));
}

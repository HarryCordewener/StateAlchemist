using System;
using StateAlchemist.Contracts;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Samples.Telnet;

namespace StateAlchemist.Generated.Tests;

/// <summary>Turns a contract shape into its generated machine.</summary>
internal static class GeneratedHarness
{
    public static IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks)
    {
        var handled = hooks is { HandleExceptions: true };
        return (shape.Name, handled) switch
        {
            ("Recorder", false) => new RecorderMachine((RecordingContext)context) { Hooks = hooks },
            ("RecorderUnchecked", false) => new RecorderUncheckedMachine((RecordingContext)context) { Hooks = hooks },
            ("Guards", false) => new GuardsMachine((RecordingContext)context) { Hooks = hooks },
            ("GuardsThatThrow", false) => new GuardsThatThrowMachine((RecordingContext)context) { Hooks = hooks },
            ("Failures", false) => new FailuresMachine((RecordingContext)context) { Hooks = hooks },
            ("Failures", true) => new FailuresHandledMachine((RecordingContext)context) { Hooks = hooks },
            ("SampleTelnet", false) => new TelnetMachine((TelnetContext)context) { Hooks = hooks },
            ("RecorderSerialized", false) => new RecorderSerializedMachine((RecordingContext)context) { Hooks = hooks },
            ("Deciding", false) => new DecidingMachine((RecordingContext)context) { Hooks = hooks },
            ("DecidingSerialized", false) => new DecidingSerializedMachine((RecordingContext)context) { Hooks = hooks },
            ("Runs", false) => new RunsMachine((RecordingContext)context) { Hooks = hooks },
            ("RunsSerialized", false) => new RunsSerializedMachine((RecordingContext)context) { Hooks = hooks },
            _ => throw new NotSupportedException($"No generated machine for shape '{shape.Name}'{(handled ? " with exception hooks" : "")}."),
        };
    }
}

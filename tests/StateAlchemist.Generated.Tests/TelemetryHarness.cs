using System;
using StateAlchemist.Contracts;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Samples.Telnet;

namespace StateAlchemist.Generated.Tests;

/// <summary>Turns a contract shape into its generated machine with <c>Telemetry</c> set.</summary>
internal static class TelemetryHarness
{
    public static IMachine<byte> Create(MachineShape shape, object context, ContractHooks? hooks)
    {
        var handled = hooks is { HandleExceptions: true };
        return (shape.Name, handled) switch
        {
            ("Recorder", false) => new TelemetryRecorderMachine((RecordingContext)context) { Hooks = hooks },
            ("RecorderUnchecked", false) => new TelemetryRecorderUncheckedMachine((RecordingContext)context) { Hooks = hooks },
            ("Guards", false) => new TelemetryGuardsMachine((RecordingContext)context) { Hooks = hooks },
            ("GuardsThatThrow", false) => new TelemetryGuardsThatThrowMachine((RecordingContext)context) { Hooks = hooks },
            ("Failures", false) => new TelemetryFailuresMachine((RecordingContext)context) { Hooks = hooks },
            ("Failures", true) => new TelemetryFailuresHandledMachine((RecordingContext)context) { Hooks = hooks },
            ("SampleTelnet", false) => new TelemetryTelnetMachine((TelnetContext)context) { Hooks = hooks },
            ("RecorderSerialized", false) => new TelemetryRecorderSerializedMachine((RecordingContext)context) { Hooks = hooks },
            ("Deciding", false) => new TelemetryDecidingMachine((RecordingContext)context) { Hooks = hooks },
            ("DecidingSerialized", false) => new TelemetryDecidingSerializedMachine((RecordingContext)context) { Hooks = hooks },
            ("Runs", false) => new TelemetryRunsMachine((RecordingContext)context) { Hooks = hooks },
            ("RunsSerialized", false) => new TelemetryRunsSerializedMachine((RecordingContext)context) { Hooks = hooks },
            ("Joins", false) => new TelemetryJoinsMachine((RecordingContext)context) { Hooks = hooks },
            _ => throw new NotSupportedException($"No telemetry machine for shape '{shape.Name}'{(handled ? " with exception hooks" : "")}."),
        };
    }
}

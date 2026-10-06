using System;
using StateAlchemist.Contracts;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Deciding;
using StateAlchemist.Contracts.Machines.Failures;
using StateAlchemist.Contracts.Machines.Guards;
using StateAlchemist.Contracts.Machines.Joins;
using StateAlchemist.Contracts.Machines.Recording;
using StateAlchemist.Contracts.Machines.Runs;
using StateAlchemist.Contracts.Machines.Timing;
using StateAlchemist.Samples.Telnet;

namespace StateAlchemist.Generated.Tests;

// The contract machines again with Telemetry set: the telemetry contracts run the whole suite against them, so
// the activities and instruments are shown to change nothing the machine does.

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(RecordingContext), Telemetry = true)]
[Include(typeof(RecorderModule)), Include(typeof(RecorderExtras))]
public sealed partial class TelemetryRecorderMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);

    partial void OnUnhandled(StateId state, byte value) => Hooks?.UnhandledValue(StateType, value);

    partial void OnUnhandled(StateId state, in Ping e) => Hooks?.UnhandledEvent(StateType, e);
}

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(RecordingContext), Concurrency = Concurrency.Unchecked, Telemetry = true)]
[Include(typeof(RecorderModule)), Include(typeof(RecorderExtras))]
public sealed partial class TelemetryRecorderUncheckedMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);
}

[Machine(Root = typeof(GuardRoot), Value = typeof(byte), Context = typeof(RecordingContext), Telemetry = true)]
[Include(typeof(GuardModule))]
public sealed partial class TelemetryGuardsMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);

    partial void OnUnhandled(StateId state, byte value) => Hooks?.UnhandledValue(StateType, value);
}

[Machine(Root = typeof(GuardRoot), Value = typeof(byte), Context = typeof(RecordingContext), Unhandled = Unhandled.Throw, Telemetry = true)]
[Include(typeof(GuardModule))]
public sealed partial class TelemetryGuardsThatThrowMachine
{
    public ContractHooks? Hooks { get; set; }
}

[Machine(Root = typeof(FailRoot), Value = typeof(byte), Context = typeof(RecordingContext), Telemetry = true)]
[Include(typeof(FailureModule))]
public sealed partial class TelemetryFailuresMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);

    partial void OnUnhandled(StateId state, byte value) => Hooks?.UnhandledValue(StateType, value);
}

[Machine(Root = typeof(FailRoot), Value = typeof(byte), Context = typeof(RecordingContext), Telemetry = true)]
[Include(typeof(FailureModule))]
public sealed partial class TelemetryFailuresHandledMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);

    partial void OnUnhandled(StateId state, byte value) => Hooks?.UnhandledValue(StateType, value);

    partial void OnGuardException(Exception exception, in TransitionInfo<byte> transition, ref ExceptionResolution resolution) => resolution = Hooks!.Exception(exception, transition);

    partial void OnTransformException(Exception exception, in TransitionInfo<byte> transition, ref ExceptionResolution resolution) => resolution = Hooks!.Exception(exception, transition);

    partial void OnExitedException(Exception exception, in TransitionInfo<byte> transition, ref ExceptionResolution resolution) => resolution = Hooks!.Exception(exception, transition);

    partial void OnEnteredException(Exception exception, in TransitionInfo<byte> transition, ref ExceptionResolution resolution) => resolution = Hooks!.Exception(exception, transition);

    partial void OnCompletedException(Exception exception, in TransitionInfo<byte> transition, ref ExceptionResolution resolution) => resolution = Hooks!.Exception(exception, transition);
}

[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext), Telemetry = true)]
[Include(typeof(TelnetCore)), Include(typeof(GmcpModule)), Include(typeof(NawsModule))]
public sealed partial class TelemetryTelnetMachine
{
    public ContractHooks? Hooks { get; set; }
}

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(RecordingContext), Concurrency = Concurrency.Serialized, Telemetry = true)]
[Include(typeof(RecorderModule)), Include(typeof(RecorderExtras))]
public sealed partial class TelemetryRecorderSerializedMachine
{
    public ContractHooks? Hooks { get; set; }
}

[Machine(Root = typeof(DecideRoot), Value = typeof(byte), Context = typeof(RecordingContext), Telemetry = true)]
[Include(typeof(DecidingModule))]
public sealed partial class TelemetryDecidingMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);

    partial void OnUnhandled(StateId state, byte value) => Hooks?.UnhandledValue(StateType, value);

    partial void OnUnhandled(StateId state, in DecisionFailed e) => Hooks?.UnhandledEvent(StateType, e);
}

[Machine(Root = typeof(DecideRoot), Value = typeof(byte), Context = typeof(RecordingContext), Concurrency = Concurrency.Serialized, Telemetry = true)]
[Include(typeof(DecidingModule))]
public sealed partial class TelemetryDecidingSerializedMachine
{
    public ContractHooks? Hooks { get; set; }
}

[Machine(Root = typeof(RunRoot), Value = typeof(byte), Context = typeof(RecordingContext), Telemetry = true)]
[Include(typeof(RunModule))]
public sealed partial class TelemetryRunsMachine
{
    public ContractHooks? Hooks { get; set; }
}

[Machine(Root = typeof(RunRoot), Value = typeof(byte), Context = typeof(RecordingContext), Concurrency = Concurrency.Serialized, Telemetry = true)]
[Include(typeof(RunModule))]
public sealed partial class TelemetryRunsSerializedMachine
{
    public ContractHooks? Hooks { get; set; }
}

[Machine(Root = typeof(JoinRoot), Value = typeof(byte), Context = typeof(RecordingContext), Telemetry = true)]
[Include(typeof(JoinModule))]
public sealed partial class TelemetryJoinsMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);
}

[Machine(Root = typeof(TimeRoot), Value = typeof(byte), Context = typeof(RecordingContext), Telemetry = true)]
[Include(typeof(TimingModule))]
public sealed partial class TelemetryTimingMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);

    partial void OnTimerException(Exception exception, string transition)
    {
        if (Hooks is { HandleTimerExceptions: true })
        {
            Hooks.TimerException(exception, transition);
        }
    }
}

[Machine(Root = typeof(TimeRoot), Value = typeof(byte), Context = typeof(RecordingContext), Concurrency = Concurrency.Serialized, Telemetry = true)]
[Include(typeof(TimingModule))]
public sealed partial class TelemetryTimingSerializedMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);

    partial void OnTimerException(Exception exception, string transition) => Hooks?.TimerException(exception, transition);
}

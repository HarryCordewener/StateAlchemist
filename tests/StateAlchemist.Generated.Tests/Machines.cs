using System;
using StateAlchemist.Contracts;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Deciding;
using StateAlchemist.Contracts.Machines.Failures;
using StateAlchemist.Contracts.Machines.Guards;
using StateAlchemist.Contracts.Machines.Recording;
using StateAlchemist.Contracts.Machines.Runs;
using StateAlchemist.Samples.Telnet;

namespace StateAlchemist.Generated.Tests;

// One [Machine] per contract shape, with the same modules the shape names. Each forwards its hooks to the test's
// ContractHooks; the "Handled" variant also implements the exception hooks, so the generator writes their try/catch.

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(RecordingContext))]
[Include(typeof(RecorderModule)), Include(typeof(RecorderExtras))]
public sealed partial class RecorderMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);

    partial void OnUnhandled(StateId state, byte value) => Hooks?.UnhandledValue(StateType, value);

    partial void OnUnhandled(StateId state, in Ping e) => Hooks?.UnhandledEvent(StateType, e);
}

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(RecordingContext), Concurrency = Concurrency.Unchecked)]
[Include(typeof(RecorderModule)), Include(typeof(RecorderExtras))]
public sealed partial class RecorderUncheckedMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);
}

[Machine(Root = typeof(GuardRoot), Value = typeof(byte), Context = typeof(RecordingContext))]
[Include(typeof(GuardModule))]
public sealed partial class GuardsMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);

    partial void OnUnhandled(StateId state, byte value) => Hooks?.UnhandledValue(StateType, value);
}

[Machine(Root = typeof(GuardRoot), Value = typeof(byte), Context = typeof(RecordingContext), Unhandled = Unhandled.Throw)]
[Include(typeof(GuardModule))]
public sealed partial class GuardsThatThrowMachine
{
    public ContractHooks? Hooks { get; set; }
}

[Machine(Root = typeof(FailRoot), Value = typeof(byte), Context = typeof(RecordingContext))]
[Include(typeof(FailureModule))]
public sealed partial class FailuresMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);

    partial void OnUnhandled(StateId state, byte value) => Hooks?.UnhandledValue(StateType, value);
}

[Machine(Root = typeof(FailRoot), Value = typeof(byte), Context = typeof(RecordingContext))]
[Include(typeof(FailureModule))]
public sealed partial class FailuresHandledMachine
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

[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext))]
[Include(typeof(TelnetCore)), Include(typeof(GmcpModule)), Include(typeof(NawsModule))]
public sealed partial class TelnetMachine
{
    public ContractHooks? Hooks { get; set; }
}

[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(RecordingContext), Concurrency = Concurrency.Serialized)]
[Include(typeof(RecorderModule)), Include(typeof(RecorderExtras))]
public sealed partial class RecorderSerializedMachine
{
    public ContractHooks? Hooks { get; set; }
}

[Machine(Root = typeof(DecideRoot), Value = typeof(byte), Context = typeof(RecordingContext))]
[Include(typeof(DecidingModule))]
public sealed partial class DecidingMachine
{
    public ContractHooks? Hooks { get; set; }

    partial void OnTransitioned(in TransitionInfo<byte> transition) => Hooks?.Transitioned(transition);

    partial void OnUnhandled(StateId state, byte value) => Hooks?.UnhandledValue(StateType, value);

    partial void OnUnhandled(StateId state, in DecisionFailed e) => Hooks?.UnhandledEvent(StateType, e);
}

[Machine(Root = typeof(DecideRoot), Value = typeof(byte), Context = typeof(RecordingContext), Concurrency = Concurrency.Serialized)]
[Include(typeof(DecidingModule))]
public sealed partial class DecidingSerializedMachine
{
    public ContractHooks? Hooks { get; set; }
}

[Machine(Root = typeof(RunRoot), Value = typeof(byte), Context = typeof(RecordingContext))]
[Include(typeof(RunModule))]
public sealed partial class RunsMachine
{
    public ContractHooks? Hooks { get; set; }
}
